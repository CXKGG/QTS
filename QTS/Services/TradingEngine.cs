using System.Text.Json;
using QTS.Contracts;
using QTS.Domain;
using QTS.Infrastructure;
using QTS.Services;
using SqlSugar;
using TaskStatus = QTS.Domain.TaskStatus;

namespace QTS.Services;

/// <summary>
/// 交易引擎：只在新 K 线出现时执行策略信号。策略目标仓位 1=做多、0=空仓、-1=做空。
/// 模拟盘支持多/空与杠杆；实盘必须同时满足“已配置凭据 + 用户主动开启实盘开关 + 任务模式为 Live”。
/// Live 的 1× 多单走币币现货，杠杆>1 或空头走 USDT 永续（逐仓、reduceOnly 平仓）。
/// </summary>
public sealed class TradingEngine : ITradingEngine
{
    private const int MaxLeverage = 20;

    private readonly ISqlSugarClient _db;
    private readonly IStrategyRegistry _strategies;
    private readonly IMarketDataService _market;
    private readonly IPaperAccountService _paper;
    private readonly IOkxConfigService _okxConfig;
    private readonly IOkxClient _okx;
    private readonly ILogger<TradingEngine> _logger;

    public TradingEngine(
        ISqlSugarClient db,
        IStrategyRegistry strategies,
        IMarketDataService market,
        IPaperAccountService paper,
        IOkxConfigService okxConfig,
        IOkxClient okx,
        ILogger<TradingEngine> logger)
    {
        _db = db;
        _strategies = strategies;
        _market = market;
        _paper = paper;
        _okxConfig = okxConfig;
        _okx = okx;
        _logger = logger;
    }

    public async Task ExecuteTaskAsync(long taskId, CancellationToken ct = default)
    {
        var task = await _db.Queryable<TaskEntity>().FirstAsync(x => x.Id == taskId);
        if (task is null || task.Status != TaskStatus.Running) return;

        try
        {
            await ExecuteCoreAsync(task, ct);
        }
        catch (BusinessException ex)
        {
            await MarkErrorAsync(task, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "任务 {TaskId} 执行失败", task.Id);
            await MarkErrorAsync(task, ex.Message);
        }
    }

    public async Task<bool> CloseTaskPositionAsync(long taskId, CancellationToken ct = default)
    {
        var task = await _db.Queryable<TaskEntity>().FirstAsync(x => x.Id == taskId)
                   ?? throw new BusinessException("任务不存在", "NOT_FOUND", 404);
        var position = await _db.Queryable<PositionEntity>()
            .Where(x => x.TaskId == taskId && x.Status == PositionStatus.Open)
            .OrderByDescending(x => x.Id)
            .FirstAsync();
        if (position is null)
            throw new BusinessException("该任务当前没有未平仓持仓");

        var spotInstId = position.InstId.EndsWith("-SWAP", StringComparison.OrdinalIgnoreCase)
            ? position.InstId[..^5]
            : position.InstId;
        decimal price;
        try
        {
            price = await _market.GetLatestPriceAsync(spotInstId, ct);
        }
        catch
        {
            price = position.LastPrice > 0m ? position.LastPrice : position.AvgEntryPrice;
        }
        if (price <= 0m) throw new BusinessException("无法获取最新价格，平仓失败");

        await ExitPositionAsync(task, position, price, "手动平仓", ct);
        return true;
    }

    private async Task ExecuteCoreAsync(TaskEntity task, CancellationToken ct)
    {
        var strategy = _strategies.Get(task.StrategyCode);
        if (strategy is null) throw new BusinessException($"策略不存在：{task.StrategyCode}");

        var span = MarketBars.GetSpan(task.Bar);
        var end = DateTime.UtcNow;
        var start = end - TimeSpan.FromTicks(span.Ticks * 400L);
        var candleSet = await _market.EnsureCandlesAsync(task.InstId, task.Bar, start, end, false, ct);
        var candles = candleSet.Candles;
        if (candles.Count < 50)
            throw new BusinessException($"有效 K 线不足（{candles.Count} 根），暂时无法计算策略信号");
        if (!string.IsNullOrWhiteSpace(candleSet.Warning))
            _logger.LogWarning("任务 {TaskId} 行情回退：{Warning}", task.Id, candleSet.Warning);

        var latest = candles[^1];
        var now = DateTime.UtcNow;

        if (task.LastProcessedBarTime.HasValue && latest.OpenTime <= task.LastProcessedBarTime.Value)
        {
            task.LastRunAt = now;
            await _db.Updateable(task).UpdateColumns(x => new { x.LastRunAt }).ExecuteCommandAsync();
            return;
        }

        var parameters = DeserializeParams(task.ParamsJson);
        var signals = strategy.Evaluate(candles, parameters);
        var signal = signals.LastOrDefault();
        var targetPosition = Math.Clamp(signal?.Position ?? 0, -1, 1);
        var reason = string.IsNullOrWhiteSpace(signal?.Reason) ? "无信号" : signal!.Reason;

        var risk = DeserializeRisk(task.RiskJson);
        var leverage = Math.Clamp(task.Leverage, 1, MaxLeverage);
        var slippagePct = Math.Clamp(task.SlippagePct, 0m, 5m);
        var position = await _db.Queryable<PositionEntity>()
            .FirstAsync(x => x.TaskId == task.Id && x.Status == PositionStatus.Open);

        if (position is not null)
        {
            var direction = IsShort(position.Side) ? -1m : 1m;
            position.LastPrice = latest.Close;
            position.UnrealizedPnl = Math.Round((latest.Close - position.AvgEntryPrice) * position.Quantity * direction, 8);
            position.UpdatedAt = now;
            await _db.Updateable(position)
                .UpdateColumns(x => new { x.LastPrice, x.UnrealizedPnl, x.UpdatedAt })
                .ExecuteCommandAsync();
        }

        var riskExited = false;
        if (position is not null)
        {
            var (riskExitPrice, riskReason) = EvaluateRiskExit(position, latest);
            if (riskExitPrice > 0m)
            {
                await ExitPositionAsync(task, position, riskExitPrice, riskReason, ct);
                position = null;
                riskExited = true;
            }
            else if (targetPosition != Direction(position))
            {
                var direction = Direction(position);
                var exitReason = targetPosition == 0
                    ? reason
                    : targetPosition > 0 ? "策略转向做多，平空反手" : "策略转向做空，平多反手";
                await ExitPositionAsync(task, position, latest.Close, exitReason, ct);
                position = null;
            }
        }

        if (task.Status == TaskStatus.Error) return;

        // 反手允许在同一根 K 线完成；止损离场则等待策略先回到空仓，避免立即追单。
        if (!riskExited && position is null && targetPosition != 0)
        {
            await EnterPositionAsync(task, targetPosition, latest.Close, risk, leverage, slippagePct, reason, ct);
            position = await _db.Queryable<PositionEntity>()
                .FirstAsync(x => x.TaskId == task.Id && x.Status == PositionStatus.Open);
        }

        task.LastRunAt = now;
        task.LastProcessedBarTime = latest.Time > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(latest.Time).UtcDateTime
            : latest.OpenTime;
        task.LastSignal = position is null ? "FLAT" : IsShort(position.Side) ? "SHORT" : "LONG";
        task.LastError = null;
        task.UpdatedAt = now;
        await _db.Updateable(task)
            .UpdateColumns(x => new
            {
                x.LastRunAt, x.LastProcessedBarTime, x.LastSignal, x.LastError, x.UpdatedAt, x.TotalPnl
            })
            .ExecuteCommandAsync();

        if (task.Mode == TradingMode.Paper)
            await _paper.RecordSnapshotAsync(ct);
    }

    private static (decimal Price, string Reason) EvaluateRiskExit(PositionEntity position, CandlePoint latest)
    {
        var isShort = IsShort(position.Side);
        if (isShort)
        {
            if (position.StopLoss > 0m && latest.High >= position.StopLoss) return (position.StopLoss, "触发止损");
            if (position.TakeProfit > 0m && latest.Low <= position.TakeProfit) return (position.TakeProfit, "触发止盈");
            return (0m, "");
        }

        if (position.StopLoss > 0m && latest.Low <= position.StopLoss) return (position.StopLoss, "触发止损");
        if (position.TakeProfit > 0m && latest.High >= position.TakeProfit) return (position.TakeProfit, "触发止盈");
        return (0m, "");
    }

    private async Task EnterPositionAsync(
        TaskEntity task, int targetPosition, decimal marketPrice, TaskRiskDto risk,
        int leverage, decimal slippagePct, string reason, CancellationToken ct)
    {
        var isLong = targetPosition > 0;
        var side = isLong ? OrderSide.Buy : OrderSide.Sell;
        var maxPositionPct = Math.Clamp(risk.MaxPositionPct, 1m, 100m);
        var allocated = task.AllocatedCapital * maxPositionPct / 100m;
        var action = isLong ? "做多" : "做空";
        var orderReason = $"{action}信号：{reason}";

        if (task.Mode == TradingMode.Paper)
        {
            var account = await _paper.GetOrCreateAsync(ct);
            // 预留 0.2% 手续费缓冲；可用资金不足时自动缩小保证金，而不是直接跳过信号。
            var margin = Math.Round(Math.Min(allocated, Math.Max(0m, account.Balance / 1.002m)), 2);
            if (margin < 1m)
                throw new BusinessException("模拟盘可用资金不足，无法开仓");

            var (paperOrder, error) = await _paper.OpenAsync(
                task.Id, task.Name, task.InstId, side, margin, leverage, marketPrice, slippagePct, orderReason, ct);
            if (paperOrder is null)
                throw new BusinessException(error ?? "模拟盘开仓失败");

            await CreatePaperPositionAsync(task, paperOrder, margin, leverage, risk, ct);
            return;
        }

        var config = await _okxConfig.GetAsync(ct);
        if (!config.HasCredentials)
            throw new BusinessException("实盘任务未配置欧易 API 凭据，请先到设置页完成配置");
        if (!config.LiveTradingEnabled)
            throw new BusinessException("实盘开关未开启，任务已暂停以避免误下单");

        // 1x 多单走现货；杠杆或空头走 USDT 永续合约（逐仓）。
        if (isLong && leverage <= 1)
        {
            var quantity = Math.Round(allocated / marketPrice, 8);
            if (quantity <= 0m) throw new BusinessException("下单数量过小，请增加分配资金");

            var clientOrderId = $"qts{task.Id}{DateTime.UtcNow:MMddHHmmss}";
            OkxResult<OkxOrderFill> fill;
            try
            {
                fill = await _okx.PlaceMarketOrderAsync(task.InstId, side, quantity, clientOrderId, ct);
            }
            catch (Exception ex)
            {
                fill = OkxResult<OkxOrderFill>.Fail(ex.Message);
            }

            if (!fill.Success || fill.Data is null)
            {
                await SaveFailedOrderAsync(task, side, marketPrice, quantity, fill.Error, ct);
                throw new BusinessException($"欧易市价买入失败：{fill.Error}");
            }

            var avgPrice = fill.Data.AvgPrice > 0m ? fill.Data.AvgPrice : marketPrice;
            var order = new OrderEntity
            {
                TaskId = task.Id,
                TaskName = task.Name,
                InstId = task.InstId,
                Mode = TradingMode.Live,
                Side = side,
                OrderType = "Market",
                Price = marketPrice,
                Quantity = quantity,
                FilledQuantity = fill.Data.FilledQuantity > 0m ? fill.Data.FilledQuantity : quantity,
                AvgFillPrice = avgPrice,
                Fee = fill.Data.Fee,
                Leverage = 1,
                CtVal = 1m,
                Status = OrderStatus.Filled,
                OkxOrderId = fill.Data.OrderId,
                ClientOrderId = clientOrderId,
                Reason = orderReason,
                IsPaper = false,
                CreatedAt = DateTime.UtcNow,
                FilledAt = DateTime.UtcNow
            };
            order.Id = await _db.Insertable(order).ExecuteReturnBigIdentityAsync();
            await CreateLivePositionAsync(task, order, risk, ct);
            return;
        }

        await EnterLiveContractAsync(task, isLong, marketPrice, allocated, leverage, risk, orderReason, ct);
    }

    /// <summary>实盘合约开仓：按逐仓保证金计算张数，支持多/空与杠杆。</summary>
    private async Task EnterLiveContractAsync(
        TaskEntity task, bool isLong, decimal marketPrice, decimal margin, int leverage,
        TaskRiskDto risk, string orderReason, CancellationToken ct)
    {
        var swapInstId = ToSwapInstId(task.InstId);
        var accountConfigResult = await _okx.GetAccountConfigAsync(ct);
        if (!accountConfigResult.Success || accountConfigResult.Data is null)
            throw new BusinessException($"读取欧易账户持仓模式失败：{accountConfigResult.Error}");
        var accountConfig = accountConfigResult.Data;
        var posSide = accountConfig.IsLongShortMode ? (isLong ? "long" : "short") : null;

        var specResult = await _okx.GetSwapInstrumentAsync(swapInstId, ct);
        if (!specResult.Success || specResult.Data is null)
            throw new BusinessException($"读取合约规格失败：{specResult.Error}");
        var spec = specResult.Data;

        // OKX returns 59103 when the requested leverage cannot support even the minimum contract at
        // the current equity. Check the minimum notional locally first so the user gets an actionable
        // message instead of a raw OKX error.
        var minNotional = spec.MinSz * spec.CtVal * marketPrice;
        var maxNotional = margin * leverage;
        if (minNotional > 0 && maxNotional < minNotional)
        {
            var suggestedLeverage = Math.Clamp(
                (int)Math.Ceiling(minNotional / Math.Max(margin, 0.01m)),
                1, 20);
            var leverageHint = suggestedLeverage * margin >= minNotional
                ? $"请将杠杆提高到 {suggestedLeverage}× 或以上"
                : "即使 20× 仍不足，请增加分配资金";
            throw new BusinessException(
                $"可用保证金不足：{swapInstId} 最小下单 {spec.MinSz:0.####} 张，名义价值约 {minNotional:F2} USDT；" +
                $"当前有效保证金 {margin:F2}U（分配资金 × 最大仓位%）× {leverage}× 仅 {maxNotional:F2}U。" +
                $"{leverageHint}，或改用 ETH-USDT（最小名义约 2.3U）。");
        }

        var leverageResult = await _okx.SetLeverageAsync(swapInstId, leverage, posSide, ct);
        if (!leverageResult.Success)
            throw new BusinessException($"设置欧易杠杆失败（{leverage}×）：{leverageResult.Error}。若提示保证金不足，请提高杠杆或增加分配资金。");

        // 保证金 × 杠杆 = 名义价值；张数按 ctVal 与 lotSz 向下取整。
        var notional = margin * leverage;
        var rawContracts = notional / Math.Max(marketPrice, 0.00000001m) / Math.Max(spec.CtVal, 0.00000001m);
        var contracts = FloorToStep(rawContracts, spec.LotSz);
        if (contracts < spec.MinSz)
            throw new BusinessException($"保证金不足：{swapInstId} 最小下单 {spec.MinSz} 张（每张 {spec.CtVal} 币），请增加分配资金或杠杆");

        var baseQuantity = contracts * spec.CtVal;
        var side = isLong ? OrderSide.Buy : OrderSide.Sell;
        var clientOrderId = $"qts{task.Id}{DateTime.UtcNow:MMddHHmmss}";
        OkxResult<OkxOrderFill> fill;
        try
        {
            fill = await _okx.PlaceSwapMarketOrderAsync(
                swapInstId, side, posSide, contracts, reduceOnly: false, clientOrderId, ct);
        }
        catch (Exception ex)
        {
            fill = OkxResult<OkxOrderFill>.Fail(ex.Message);
        }

        if (!fill.Success || fill.Data is null)
        {
            await SaveFailedOrderAsync(task, side, marketPrice, baseQuantity, fill.Error, ct);
            throw new BusinessException($"欧易合约开仓失败：{fill.Error}");
        }

        var entry = fill.Data.AvgPrice > 0m ? fill.Data.AvgPrice : marketPrice;
        var filledContracts = fill.Data.FilledQuantity > 0m ? fill.Data.FilledQuantity : contracts;
        var filledBase = filledContracts * spec.CtVal;
        var now = DateTime.UtcNow;
        var order = new OrderEntity
        {
            TaskId = task.Id,
            TaskName = task.Name,
            InstId = swapInstId,
            Mode = TradingMode.Live,
            Side = side,
            OrderType = "Market",
            Price = marketPrice,
            Quantity = filledBase,
            FilledQuantity = filledBase,
            AvgFillPrice = entry,
            Fee = fill.Data.Fee,
            Leverage = leverage,
            CtVal = spec.CtVal,
            Status = OrderStatus.Filled,
            OkxOrderId = fill.Data.OrderId,
            ClientOrderId = clientOrderId,
            Reason = orderReason,
            IsPaper = false,
            CreatedAt = now,
            FilledAt = now
        };
        order.Id = await _db.Insertable(order).ExecuteReturnBigIdentityAsync();

        var position = new PositionEntity
        {
            TaskId = task.Id,
            InstId = swapInstId,
            Mode = TradingMode.Live,
            Side = isLong ? "long" : "short",
            Quantity = filledBase,
            AvgEntryPrice = entry,
            LastPrice = entry,
            UnrealizedPnl = 0m,
            RealizedPnl = 0m,
            Margin = margin,
            Leverage = leverage,
            CtVal = spec.CtVal,
            StopLoss = CalcStopLoss(entry, risk.StopLossPct, isLong),
            TakeProfit = CalcTakeProfit(entry, risk.TakeProfitPct, isLong),
            Status = PositionStatus.Open,
            OpenedAt = now,
            UpdatedAt = now
        };
        position.Id = await _db.Insertable(position).ExecuteReturnBigIdentityAsync();

        task.LastSignal = isLong ? "LONG" : "SHORT";
        task.LastError = null;
        task.UpdatedAt = now;
        await _db.Updateable(task).UpdateColumns(x => new { x.LastSignal, x.LastError, x.UpdatedAt }).ExecuteCommandAsync();
    }

    private static string ToSwapInstId(string instId) =>
        instId.EndsWith("-SWAP", StringComparison.OrdinalIgnoreCase) ? instId : $"{instId}-SWAP";

    private static decimal FloorToStep(decimal value, decimal step)
    {
        if (step <= 0m) return Math.Floor(value);
        return Math.Floor(value / step) * step;
    }

    private async Task CreatePaperPositionAsync(
        TaskEntity task, OrderEntity order, decimal margin, int leverage, TaskRiskDto risk, CancellationToken ct)
    {
        var entry = order.AvgFillPrice > 0m ? order.AvgFillPrice : order.Price;
        var quantity = order.Quantity;
        var isLong = order.Side == OrderSide.Buy;
        var now = DateTime.UtcNow;
        var position = new PositionEntity
        {
            TaskId = task.Id,
            InstId = task.InstId,
            Mode = TradingMode.Paper,
            Side = isLong ? "long" : "short",
            Quantity = quantity,
            AvgEntryPrice = entry,
            LastPrice = entry,
            UnrealizedPnl = 0m,
            RealizedPnl = 0m,
            Margin = margin,
            Leverage = leverage,
            StopLoss = CalcStopLoss(entry, risk.StopLossPct, isLong),
            TakeProfit = CalcTakeProfit(entry, risk.TakeProfitPct, isLong),
            Status = PositionStatus.Open,
            OpenedAt = now,
            UpdatedAt = now
        };
        position.Id = await _db.Insertable(position).ExecuteReturnBigIdentityAsync();
        task.LastSignal = isLong ? "LONG" : "SHORT";
        task.LastError = null;
        task.UpdatedAt = now;
        await _db.Updateable(task).UpdateColumns(x => new { x.LastSignal, x.LastError, x.UpdatedAt }).ExecuteCommandAsync();
    }

    private async Task CreateLivePositionAsync(TaskEntity task, OrderEntity order, TaskRiskDto risk, CancellationToken ct)
    {
        var entry = order.AvgFillPrice > 0m ? order.AvgFillPrice : order.Price;
        var quantity = order.FilledQuantity > 0m ? order.FilledQuantity : order.Quantity;
        var isLong = order.Side == OrderSide.Buy;
        var now = DateTime.UtcNow;
        var position = new PositionEntity
        {
            TaskId = task.Id,
            InstId = task.InstId,
            Mode = TradingMode.Live,
            Side = isLong ? "long" : "short",
            Quantity = quantity,
            AvgEntryPrice = entry,
            LastPrice = entry,
            UnrealizedPnl = 0m,
            RealizedPnl = 0m,
            Margin = 0m,
            Leverage = order.Leverage,
            CtVal = 1m,
            StopLoss = CalcStopLoss(entry, risk.StopLossPct, isLong),
            TakeProfit = CalcTakeProfit(entry, risk.TakeProfitPct, isLong),
            Status = PositionStatus.Open,
            OpenedAt = now,
            UpdatedAt = now
        };
        position.Id = await _db.Insertable(position).ExecuteReturnBigIdentityAsync();
        task.LastSignal = isLong ? "LONG" : "SHORT";
        task.LastError = null;
        task.UpdatedAt = now;
        await _db.Updateable(task).UpdateColumns(x => new { x.LastSignal, x.LastError, x.UpdatedAt }).ExecuteCommandAsync();
    }

    private async Task ExitPositionAsync(TaskEntity task, PositionEntity position, decimal price, string reason, CancellationToken ct)
    {
        price = Math.Max(price, 0.00000001m);
        var now = DateTime.UtcNow;
        var isShort = IsShort(position.Side);
        decimal exitPrice;
        decimal fee;
        decimal realized;

        if (task.Mode == TradingMode.Paper)
        {
            var (order, error) = await _paper.CloseAsync(
                task.Id, task.Name, position, price, task.SlippagePct,
                isShort ? $"买入平空：{reason}" : $"卖出平多：{reason}", ct);
            if (order is null) throw new BusinessException(error ?? "模拟盘平仓失败");
            exitPrice = order.AvgFillPrice > 0m ? order.AvgFillPrice : price;
            fee = order.Fee;
            var direction = isShort ? -1m : 1m;
            realized = Math.Round(
                Math.Max((exitPrice - position.AvgEntryPrice) * position.Quantity * direction - fee, -position.Margin), 8);
        }
        else if (position.InstId.EndsWith("-SWAP", StringComparison.OrdinalIgnoreCase))
        {
            (exitPrice, fee, realized) = await ExitLiveContractAsync(task, position, price, reason, ct);
        }
        else
        {
            var quantity = position.Quantity;
            var clientOrderId = $"qts{task.Id}{DateTime.UtcNow:MMddHHmmss}";
            OkxResult<OkxOrderFill> fill;
            try
            {
                fill = await _okx.PlaceMarketOrderAsync(task.InstId, OrderSide.Sell, quantity, clientOrderId, ct);
            }
            catch (Exception ex)
            {
                fill = OkxResult<OkxOrderFill>.Fail(ex.Message);
            }
            if (!fill.Success || fill.Data is null)
            {
                await SaveFailedOrderAsync(task, OrderSide.Sell, price, quantity, fill.Error, ct);
                throw new BusinessException($"欧易市价卖出失败：{fill.Error}");
            }

            exitPrice = fill.Data.AvgPrice > 0m ? fill.Data.AvgPrice : price;
            fee = fill.Data.Fee;
            realized = Math.Round((exitPrice - position.AvgEntryPrice) * quantity - fee, 8);
            var order = new OrderEntity
            {
                TaskId = task.Id,
                TaskName = task.Name,
                InstId = task.InstId,
                Mode = TradingMode.Live,
                Side = OrderSide.Sell,
                OrderType = "Market",
                Price = price,
                Quantity = quantity,
                FilledQuantity = fill.Data.FilledQuantity > 0m ? fill.Data.FilledQuantity : quantity,
                AvgFillPrice = exitPrice,
                Fee = fee,
                Leverage = 1,
                CtVal = 1m,
                Status = OrderStatus.Filled,
                OkxOrderId = fill.Data.OrderId,
                ClientOrderId = clientOrderId,
                Reason = $"卖出信号：{reason}",
                IsPaper = false,
                CreatedAt = now,
                FilledAt = now
            };
            order.Id = await _db.Insertable(order).ExecuteReturnBigIdentityAsync();
        }

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

        task.TotalPnl += realized;
        task.LastSignal = "FLAT";
        task.LastError = null;
        task.UpdatedAt = now;
        await _db.Updateable(task)
            .UpdateColumns(x => new { x.TotalPnl, x.LastSignal, x.LastError, x.UpdatedAt })
            .ExecuteCommandAsync();
        _logger.LogInformation("任务 {TaskId} 平仓 {InstId} {Side}，价格 {Price}，已实现盈亏 {Pnl}",
            task.Id, task.InstId, isShort ? "空头" : "多头", exitPrice, realized);
    }

    /// <summary>实盘合约平仓：reduceOnly + 方向感知，按基础币数量计算盈亏。</summary>
    private async Task<(decimal ExitPrice, decimal Fee, decimal Realized)> ExitLiveContractAsync(
        TaskEntity task, PositionEntity position, decimal marketPrice, string reason, CancellationToken ct)
    {
        var swapInstId = position.InstId;
        var accountConfigResult = await _okx.GetAccountConfigAsync(ct);
        if (!accountConfigResult.Success || accountConfigResult.Data is null)
            throw new BusinessException($"读取欧易账户持仓模式失败：{accountConfigResult.Error}");
        var accountConfig = accountConfigResult.Data;

        var specResult = await _okx.GetSwapInstrumentAsync(swapInstId, ct);
        if (!specResult.Success || specResult.Data is null)
            throw new BusinessException($"读取合约规格失败：{specResult.Error}");
        var spec = specResult.Data;

        var isShort = IsShort(position.Side);
        var contracts = FloorToStep(position.Quantity / Math.Max(position.CtVal, 0.00000001m), spec.LotSz);
        if (contracts < spec.MinSz)
            throw new BusinessException($"合约平仓张数低于最小下单量 {spec.MinSz} 张");

        var side = isShort ? OrderSide.Buy : OrderSide.Sell;
        var posSide = accountConfig.IsLongShortMode ? (isShort ? "short" : "long") : null;
        var clientOrderId = $"qts{task.Id}{DateTime.UtcNow:MMddHHmmss}";
        OkxResult<OkxOrderFill> fill;
        try
        {
            fill = await _okx.PlaceSwapMarketOrderAsync(
                swapInstId, side, posSide, contracts, reduceOnly: true, clientOrderId, ct);
        }
        catch (Exception ex)
        {
            fill = OkxResult<OkxOrderFill>.Fail(ex.Message);
        }

        if (!fill.Success || fill.Data is null)
        {
            await SaveFailedOrderAsync(task, side, marketPrice, position.Quantity, fill.Error, ct);
            throw new BusinessException($"欧易合约平仓失败：{fill.Error}");
        }

        var exitPrice = fill.Data.AvgPrice > 0m ? fill.Data.AvgPrice : marketPrice;
        var fee = fill.Data.Fee;
        var direction = isShort ? -1m : 1m;
        var realized = Math.Round(
            Math.Max((exitPrice - position.AvgEntryPrice) * position.Quantity * direction - fee, -position.Margin), 8);

        var now = DateTime.UtcNow;
        var order = new OrderEntity
        {
            TaskId = task.Id,
            TaskName = task.Name,
            InstId = swapInstId,
            Mode = TradingMode.Live,
            Side = side,
            OrderType = "Market",
            Price = marketPrice,
            Quantity = position.Quantity,
            FilledQuantity = position.Quantity,
            AvgFillPrice = exitPrice,
            Fee = fee,
            Leverage = position.Leverage,
            CtVal = position.CtVal,
            Status = OrderStatus.Filled,
            OkxOrderId = fill.Data.OrderId,
            ClientOrderId = clientOrderId,
            Reason = $"{(isShort ? "买入平空" : "卖出平多")}：{reason}",
            IsPaper = false,
            CreatedAt = now,
            FilledAt = now
        };
        order.Id = await _db.Insertable(order).ExecuteReturnBigIdentityAsync();
        return (exitPrice, fee, realized);
    }

    private async Task SaveFailedOrderAsync(TaskEntity task, OrderSide side, decimal price, decimal quantity, string? error, CancellationToken ct)
    {
        try
        {
            await _db.Insertable(new OrderEntity
            {
                TaskId = task.Id,
                TaskName = task.Name,
                InstId = task.InstId,
                Mode = task.Mode,
                Side = side,
                OrderType = "Market",
                Price = price,
                Quantity = quantity,
                FilledQuantity = 0m,
                AvgFillPrice = 0m,
                Fee = 0m,
                Leverage = Math.Clamp(task.Leverage, 1, MaxLeverage),
                Status = OrderStatus.Failed,
                Error = error,
                ClientOrderId = $"qts{task.Id}{DateTime.UtcNow:MMddHHmmss}",
                Reason = "下单失败",
                IsPaper = false,
                CreatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录失败订单时出错");
        }
    }

    public async Task RefreshPaperPositionsAsync(CancellationToken ct = default)
    {
        var positions = await _db.Queryable<PositionEntity>()
            .Where(x => x.Mode == TradingMode.Paper && x.Status == PositionStatus.Open)
            .ToListAsync();
        if (positions.Count == 0)
        {
            await _paper.RecordSnapshotAsync(ct);
            return;
        }

        foreach (var group in positions.GroupBy(x => x.InstId))
        {
            decimal price;
            try
            {
                price = await _market.GetLatestPriceAsync(group.Key, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "刷新持仓价格失败 {InstId}", group.Key);
                continue;
            }

            foreach (var position in group)
            {
                var direction = IsShort(position.Side) ? -1m : 1m;
                position.LastPrice = price;
                position.UnrealizedPnl = Math.Round((price - position.AvgEntryPrice) * position.Quantity * direction, 8);
                position.UpdatedAt = DateTime.UtcNow;
                await _db.Updateable(position)
                    .UpdateColumns(x => new { x.LastPrice, x.UnrealizedPnl, x.UpdatedAt })
                    .ExecuteCommandAsync();
            }
        }

        await _paper.RecordSnapshotAsync(ct);
    }

    private async Task MarkErrorAsync(TaskEntity task, string message)
    {
        try
        {
            task.Status = TaskStatus.Error;
            task.LastError = message.Length > 900 ? message[..900] : message;
            task.LastRunAt = DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            await _db.Updateable(task)
                .UpdateColumns(x => new { x.Status, x.LastError, x.LastRunAt, x.UpdatedAt })
                .ExecuteCommandAsync();
            _logger.LogWarning("任务 {TaskId} 已置为 Error：{Message}", task.Id, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "标记任务失败状态时异常");
        }
    }

    private static int Direction(PositionEntity position) => IsShort(position.Side) ? -1 : 1;

    private static bool IsShort(string? side) =>
        string.Equals(side, "short", StringComparison.OrdinalIgnoreCase);

    private static decimal CalcStopLoss(decimal entry, decimal stopLossPct, bool isLong) =>
        stopLossPct > 0m
            ? Math.Round(isLong ? entry * (1m - stopLossPct / 100m) : entry * (1m + stopLossPct / 100m), 8)
            : 0m;

    private static decimal CalcTakeProfit(decimal entry, decimal takeProfitPct, bool isLong) =>
        takeProfitPct > 0m
            ? Math.Round(isLong ? entry * (1m + takeProfitPct / 100m) : entry * (1m - takeProfitPct / 100m), 8)
            : 0m;

    private static IReadOnlyDictionary<string, object?> DeserializeParams(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, QtsJson.Options);
            if (raw is null) return new Dictionary<string, object?>();
            return raw.ToDictionary(x => x.Key, x => ToClrValue(x.Value));
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private static TaskRiskDto DeserializeRisk(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new TaskRiskDto();
        try
        {
            return JsonSerializer.Deserialize<TaskRiskDto>(json, QtsJson.Options) ?? new TaskRiskDto();
        }
        catch
        {
            return new TaskRiskDto();
        }
    }

    private static object? ToClrValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDecimal(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.Clone()
    };
}
