using QTS.Contracts;
using QTS.Domain;
using SqlSugar;

namespace QTS.Services;

/// <summary>
/// 手动交易服务：直接使用已配置的 OKX API 账户，通过 USDT 永续合约逐仓下单。
/// 手动仓位以 TaskId=0 保存，与策略任务隔离；支持多/空、1-20x 杠杆、reduceOnly 平仓。
/// 模拟环境与实盘环境由系统设置中的 OKX 配置决定。
/// </summary>
public sealed class ManualTradeService : IManualTradeService
{
    private const int MaxLeverage = 20;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ISqlSugarClient _db;
    private readonly IMarketDataService _market;
    private readonly ITaskService _tasks;
    private readonly IOkxClient _okx;
    private readonly IOkxConfigService _okxConfig;
    private readonly ILogger<ManualTradeService> _logger;

    public ManualTradeService(
        ISqlSugarClient db,
        IMarketDataService market,
        ITaskService tasks,
        IOkxClient okx,
        IOkxConfigService okxConfig,
        ILogger<ManualTradeService> logger)
    {
        _db = db;
        _market = market;
        _tasks = tasks;
        _okx = okx;
        _okxConfig = okxConfig;
        _logger = logger;
    }

    public async Task<TradeOverviewDto> GetOverviewAsync(string? instId, CancellationToken ct = default)
    {
        var rawInstruments = _market.GetInstruments();
        var instruments = rawInstruments
            .Select(x => new InstrumentDto { InstId = x.InstId, Base = x.Base, Quote = x.Quote, Name = x.Name })
            .ToList();
        var selected = NormalizeInstId(instId, rawInstruments);
        var taskPage = await _tasks.ListAsync(1, 100, null, ct);
        var taskNames = taskPage.Items.ToDictionary(x => x.Id, x => x.Name);

        var positions = await LoadOpenManualPositionsAsync(ct);
        await RefreshPricesAsync(positions, ct);

        TickerDto? ticker = null;
        try
        {
            ticker = await _market.GetTickerAsync(selected, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "交易台获取 {InstId} 行情失败", selected);
        }

        var config = await _okxConfig.GetAsync(ct);
        var account = await BuildAccountAsync(config, positions, ct);

        return new TradeOverviewDto
        {
            Account = account,
            Instruments = instruments,
            Ticker = ticker,
            Positions = positions.Select(x => MapPosition(x, "手动交易")).ToList(),
            Tasks = taskPage.Items
        };
    }

    public async Task<ManualTradeResult> OpenAsync(ManualTradeRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Gate.WaitAsync(ct);
        try
        {
            var config = await GetTradingConfigAsync(ct);
            var direction = NormalizeDirection(request.Direction);
            var spotInstId = NormalizeInstId(request.InstId, _market.GetInstruments());
            if (!_market.GetInstruments().Any(x => x.InstId == spotInstId))
                throw new BusinessException($"不支持的交易对：{request.InstId}");

            var margin = Math.Round(request.Margin, 2, MidpointRounding.AwayFromZero);
            if (margin < 1m || margin > 1_000_000m)
                throw new BusinessException("保证金必须在 1 到 1,000,000 USDT 之间");
            var leverage = Math.Clamp(request.Leverage, 1, MaxLeverage);
            var stopLossPct = Math.Clamp(request.StopLossPct, 0m, 50m);
            var takeProfitPct = Math.Clamp(request.TakeProfitPct, 0m, 200m);

            var price = await _market.GetLatestPriceAsync(spotInstId, ct);
            if (price <= 0m) throw new BusinessException("无法获取最新成交价，请稍后重试");

            var swapInstId = ToSwapInstId(spotInstId);
            var accountConfig = await GetAccountConfigAsync(ct);
            var isLong = direction == "long";
            var posSide = accountConfig.IsLongShortMode ? (isLong ? "long" : "short") : null;

            var spec = await GetSwapInstrumentAsync(swapInstId, ct);
            var leverageResult = await _okx.SetLeverageAsync(swapInstId, leverage, posSide, ct);
            if (!leverageResult.Success)
                throw new BusinessException($"设置欧易杠杆失败：{leverageResult.Error}");

            var notional = margin * leverage;
            var rawContracts = notional / Math.Max(price, 0.00000001m) / Math.Max(spec.CtVal, 0.00000001m);
            var contracts = FloorToStep(rawContracts, spec.LotSz);
            if (contracts < spec.MinSz)
                throw new BusinessException(
                    $"保证金不足：{swapInstId} 最小下单 {spec.MinSz} 张（每张 {spec.CtVal} 币），请增加保证金或杠杆");

            var side = isLong ? OrderSide.Buy : OrderSide.Sell;
            var clientOrderId = $"qtsm{DateTime.UtcNow:MMddHHmmssfff}";
            var fillResult = await _okx.PlaceSwapMarketOrderAsync(
                swapInstId, side, posSide, contracts, reduceOnly: false, clientOrderId, ct);
            if (!fillResult.Success || fillResult.Data is null)
                throw new BusinessException($"欧易合约开仓失败：{fillResult.Error}");

            var fill = fillResult.Data;
            var entry = fill.AvgPrice > 0m ? fill.AvgPrice : price;
            var filledContracts = fill.FilledQuantity > 0m ? fill.FilledQuantity : contracts;
            var filledBase = filledContracts * spec.CtVal;
            var now = DateTime.UtcNow;

            var order = new OrderEntity
            {
                TaskId = 0,
                TaskName = "手动交易",
                InstId = swapInstId,
                Mode = TradingMode.Live,
                Side = side,
                OrderType = "Market",
                Price = price,
                Quantity = filledBase,
                FilledQuantity = filledBase,
                AvgFillPrice = entry,
                Fee = fill.Fee,
                Leverage = leverage,
                CtVal = spec.CtVal,
                Status = OrderStatus.Filled,
                OkxOrderId = fill.OrderId,
                ClientOrderId = clientOrderId,
                Reason = $"手动{(isLong ? "做多" : "做空")}：保证金 {margin:F2} USDT，{leverage}×",
                IsPaper = false,
                CreatedAt = now,
                FilledAt = now
            };
            order.Id = await _db.Insertable(order).ExecuteReturnBigIdentityAsync();

            var position = new PositionEntity
            {
                TaskId = 0,
                InstId = swapInstId,
                Mode = TradingMode.Live,
                Side = direction,
                Quantity = filledBase,
                AvgEntryPrice = entry,
                LastPrice = entry,
                UnrealizedPnl = 0m,
                RealizedPnl = 0m,
                Margin = margin,
                Leverage = leverage,
                CtVal = spec.CtVal,
                StopLoss = stopLossPct > 0m
                    ? Math.Round(isLong ? entry * (1m - stopLossPct / 100m) : entry * (1m + stopLossPct / 100m), 8)
                    : 0m,
                TakeProfit = takeProfitPct > 0m
                    ? Math.Round(isLong ? entry * (1m + takeProfitPct / 100m) : entry * (1m - takeProfitPct / 100m), 8)
                    : 0m,
                Status = PositionStatus.Open,
                OpenedAt = now,
                UpdatedAt = now
            };
            position.Id = await _db.Insertable(position).ExecuteReturnBigIdentityAsync();

            var openPositions = await LoadOpenManualPositionsAsync(ct);
            await RefreshPricesAsync(openPositions, ct);
            var account = await BuildAccountAsync(config, openPositions, ct);

            _logger.LogInformation("手动合约开仓 {InstId} {Direction} {Leverage}x 张数 {Contracts} 保证金 {Margin}",
                swapInstId, direction, leverage, contracts, margin);

            var accountLabel = config.SimulatedTrading ? "模拟" : "实盘";
            return new ManualTradeResult
            {
                Order = MapOrder(order),
                Position = MapPosition(position, "手动交易"),
                Account = account,
                Message = $"{accountLabel}{(isLong ? "做多" : "做空")}成交：{filledBase:F8} {spotInstId.Split('-')[0]} @ {entry:F4}，" +
                          $"保证金 {margin:F2} USDT，杠杆 {leverage}×"
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<ManualTradeResult> CloseAsync(ManualCloseRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await Gate.WaitAsync(ct);
        try
        {
            var config = await GetTradingConfigAsync(ct);
            var position = await _db.Queryable<PositionEntity>()
                .FirstAsync(x => x.Id == request.PositionId && x.Status == PositionStatus.Open)
                ?? throw new BusinessException("持仓不存在或已平仓", "NOT_FOUND", 404);
            if (position.TaskId != 0 || position.Mode != TradingMode.Live)
                throw new BusinessException("该持仓不是手动交易仓位，请在交易任务页处理");

            var spotInstId = ToSpotInstId(position.InstId);
            var accountConfig = await GetAccountConfigAsync(ct);
            var spec = await GetSwapInstrumentAsync(position.InstId, ct);
            var contracts = FloorToStep(
                position.Quantity / Math.Max(position.CtVal, 0.00000001m), spec.LotSz);
            if (contracts < spec.MinSz)
                throw new BusinessException($"合约平仓张数低于最小下单量 {spec.MinSz} 张");

            var price = await _market.GetLatestPriceAsync(spotInstId, ct);
            if (price <= 0m) price = position.LastPrice > 0m ? position.LastPrice : position.AvgEntryPrice;

            var isShort = IsShort(position.Side);
            var side = isShort ? OrderSide.Buy : OrderSide.Sell;
            var posSide = accountConfig.IsLongShortMode ? (isShort ? "short" : "long") : null;
            var clientOrderId = $"qtsc{DateTime.UtcNow:MMddHHmmssfff}";
            var fillResult = await _okx.PlaceSwapMarketOrderAsync(
                position.InstId, side, posSide, contracts, reduceOnly: true, clientOrderId, ct);
            if (!fillResult.Success || fillResult.Data is null)
                throw new BusinessException($"欧易合约平仓失败：{fillResult.Error}");

            var fill = fillResult.Data;
            var exitPrice = fill.AvgPrice > 0m ? fill.AvgPrice : price;
            var direction = isShort ? -1m : 1m;
            var realized = Math.Round(
                Math.Max((exitPrice - position.AvgEntryPrice) * position.Quantity * direction - fill.Fee,
                    -position.Margin), 8);
            var now = DateTime.UtcNow;

            var closeOrder = new OrderEntity
            {
                TaskId = 0,
                TaskName = "手动交易",
                InstId = position.InstId,
                Mode = TradingMode.Live,
                Side = side,
                OrderType = "Market",
                Price = price,
                Quantity = position.Quantity,
                FilledQuantity = position.Quantity,
                AvgFillPrice = exitPrice,
                Fee = fill.Fee,
                Leverage = position.Leverage,
                CtVal = position.CtVal,
                Status = OrderStatus.Filled,
                OkxOrderId = fill.OrderId,
                ClientOrderId = clientOrderId,
                Reason = $"手动平仓：{(isShort ? "买入平空" : "卖出平多")}",
                IsPaper = false,
                CreatedAt = now,
                FilledAt = now
            };
            closeOrder.Id = await _db.Insertable(closeOrder).ExecuteReturnBigIdentityAsync();

            position.Status = PositionStatus.Closed;
            position.LastPrice = exitPrice;
            position.UnrealizedPnl = 0m;
            position.RealizedPnl = realized;
            position.ClosedAt = now;
            position.UpdatedAt = now;
            await _db.Updateable(position)
                .UpdateColumns(x => new
                {
                    x.Status, x.LastPrice, x.UnrealizedPnl, x.RealizedPnl, x.ClosedAt, x.UpdatedAt
                })
                .ExecuteCommandAsync();

            var openPositions = await LoadOpenManualPositionsAsync(ct);
            await RefreshPricesAsync(openPositions, ct);
            var account = await BuildAccountAsync(config, openPositions, ct);

            _logger.LogInformation("手动合约平仓 {PositionId} 价格 {Price} 已实现盈亏 {Pnl}",
                position.Id, exitPrice, realized);

            return new ManualTradeResult
            {
                Order = MapOrder(closeOrder),
                Position = MapPosition(position, "手动交易"),
                Account = account,
                Message = $"平仓成交 @ {exitPrice:F4}，已实现盈亏 {realized:F2} USDT"
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<OkxConfig> GetTradingConfigAsync(CancellationToken ct)
    {
        var config = await _okxConfig.GetAsync(ct);
        if (!config.HasCredentials)
            throw new BusinessException("请先在系统设置中配置欧易 API Key、Secret Key 和 Passphrase");
        if (!config.SimulatedTrading && !config.LiveTradingEnabled)
            throw new BusinessException("实盘开关未开启，请在系统设置中开启“允许实盘交易”后再下单");
        return config;
    }

    private async Task<OkxAccountConfig> GetAccountConfigAsync(CancellationToken ct)
    {
        var result = await _okx.GetAccountConfigAsync(ct);
        if (!result.Success || result.Data is null)
            throw new BusinessException($"读取欧易账户持仓模式失败：{result.Error}");
        return result.Data;
    }

    private async Task<OkxSwapInstrument> GetSwapInstrumentAsync(string swapInstId, CancellationToken ct)
    {
        var result = await _okx.GetSwapInstrumentAsync(swapInstId, ct);
        if (!result.Success || result.Data is null)
            throw new BusinessException($"读取合约规格失败：{result.Error}");
        return result.Data;
    }

    private async Task<List<PositionEntity>> LoadOpenManualPositionsAsync(CancellationToken ct) =>
        await _db.Queryable<PositionEntity>()
            .Where(x => x.TaskId == 0 && x.Mode == TradingMode.Live && x.Status == PositionStatus.Open)
            .OrderBy(x => x.Id, OrderByType.Desc)
            .ToListAsync(ct);

    private async Task RefreshPricesAsync(List<PositionEntity> positions, CancellationToken ct)
    {
        foreach (var position in positions)
        {
            try
            {
                var spotInstId = ToSpotInstId(position.InstId);
                var ticker = await _market.GetTickerAsync(spotInstId, ct);
                if (ticker.Last <= 0m) continue;
                var direction = IsShort(position.Side) ? -1m : 1m;
                position.LastPrice = ticker.Last;
                position.UnrealizedPnl = Math.Round(
                    (ticker.Last - position.AvgEntryPrice) * position.Quantity * direction, 8);
                position.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "刷新手动持仓 {PositionId} 标记价格失败", position.Id);
            }
        }
    }

    private async Task<TradeAccountDto> BuildAccountAsync(
        OkxConfig config, List<PositionEntity> positions, CancellationToken ct)
    {
        var account = new TradeAccountDto
        {
            Configured = config.HasCredentials,
            Simulated = config.SimulatedTrading,
            Environment = config.SimulatedTrading ? "模拟环境" : "实盘环境",
            UsedMargin = Math.Round(positions.Sum(x => x.Margin), 2),
            PositionValue = Math.Round(positions.Sum(x => Math.Abs(x.Quantity * x.LastPrice)), 2),
            RealizedPnl = Math.Round(await GetManualRealizedPnlAsync(ct), 2)
        };

        if (!config.HasCredentials)
        {
            account.Error = "未配置欧易 API 凭据，请先在系统设置中填写 API Key、Secret Key 和 Passphrase";
            return account;
        }

        var result = await _okx.GetAccountAsync(ct);
        if (!result.Success || result.Data is null)
        {
            account.Error = result.Error ?? "读取欧易账户失败";
            return account;
        }

        account.Connected = true;
        account.TotalEquity = Math.Round(result.Data.TotalEquity, 4);
        account.Available = Math.Round(result.Data.AvailableUsdt, 4);
        account.UnrealizedPnl = Math.Round(positions.Sum(x => x.UnrealizedPnl), 4);
        account.Currency = result.Data.Currency;
        return account;
    }

    private async Task<decimal> GetManualRealizedPnlAsync(CancellationToken ct)
    {
        var values = await _db.Queryable<PositionEntity>()
            .Where(x => x.TaskId == 0 && x.Mode == TradingMode.Live && x.Status == PositionStatus.Closed)
            .Select(x => x.RealizedPnl)
            .ToListAsync(ct);
        return values.Sum();
    }

    private static decimal FloorToStep(decimal value, decimal step)
    {
        if (value <= 0m) return 0m;
        if (step <= 0m) return Math.Floor(value);
        return Math.Floor(value / step) * step;
    }

    private static bool IsShort(string? side) =>
        string.Equals(side, "short", StringComparison.OrdinalIgnoreCase);

    private static string ToSwapInstId(string instId) =>
        instId.EndsWith("-SWAP", StringComparison.OrdinalIgnoreCase) ? instId : $"{instId}-SWAP";

    private static string ToSpotInstId(string instId) =>
        instId.EndsWith("-SWAP", StringComparison.OrdinalIgnoreCase)
            ? instId[..^5]
            : instId;

    private static string NormalizeDirection(string? direction)
    {
        var value = (direction ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "long" or "buy" or "多" or "做多" => "long",
            "short" or "sell" or "空" or "做空" => "short",
            _ => throw new BusinessException("交易方向必须是 long（做多）或 short（做空）")
        };
    }

    private static string NormalizeInstId(string? instId, IReadOnlyList<MarketInstrument> instruments)
    {
        var value = (instId ?? "").Trim().ToUpperInvariant();
        if (value.Length == 0) return instruments.FirstOrDefault()?.InstId ?? "BTC-USDT";
        if (value.EndsWith("-SWAP", StringComparison.OrdinalIgnoreCase))
            value = value[..^5];
        return instruments.Any(x => x.InstId == value) ? value : value;
    }

    private static OrderDto MapOrder(OrderEntity order) => new()
    {
        Id = order.Id,
        TaskId = order.TaskId,
        TaskName = order.TaskName,
        InstId = order.InstId,
        Mode = order.Mode,
        Side = order.Side,
        OrderType = order.OrderType,
        Price = order.Price,
        Quantity = order.Quantity,
        FilledQuantity = order.FilledQuantity,
        AvgFillPrice = order.AvgFillPrice,
        Fee = order.Fee,
        Leverage = order.Leverage,
        Status = order.Status,
        OkxOrderId = order.OkxOrderId,
        Error = order.Error,
        Reason = order.Reason,
        CreatedAt = order.CreatedAt,
        FilledAt = order.FilledAt
    };

    private static PositionDto MapPosition(PositionEntity position, string taskName)
    {
        var isShort = IsShort(position.Side);
        return new PositionDto
        {
            Id = position.Id,
            TaskId = position.TaskId,
            TaskName = taskName,
            InstId = position.InstId,
            Mode = position.Mode,
            Side = isShort ? "short" : "long",
            Quantity = position.Quantity,
            AvgEntryPrice = position.AvgEntryPrice,
            LastPrice = position.LastPrice,
            UnrealizedPnl = position.UnrealizedPnl,
            UnrealizedPnlPct = position.AvgEntryPrice <= 0m
                ? 0m
                : Math.Round((isShort
                    ? position.AvgEntryPrice - position.LastPrice
                    : position.LastPrice - position.AvgEntryPrice) / position.AvgEntryPrice * 100m, 4),
            Margin = position.Margin,
            Leverage = position.Leverage,
            StopLoss = position.StopLoss,
            TakeProfit = position.TakeProfit,
            OpenedAt = position.OpenedAt
        };
    }
}
