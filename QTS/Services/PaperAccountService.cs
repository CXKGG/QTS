using QTS.Domain;
using QTS.Services;
using SqlSugar;

using TaskStatus = QTS.Domain.TaskStatus;

namespace QTS.Services;

/// <summary>
/// 模拟盘账户：100 USDT 起始资金，支持多/空与 1-20x 杠杆。
/// 采用逐仓保证金模型：开仓冻结 margin，权益 = 可用现金 + 占用保证金 + 浮动盈亏；
/// 平仓返还保证金并结算盈亏，最大亏损不超过该仓保证金（简化强平）。手续费 0.1%。
/// </summary>
public sealed class PaperAccountService : IPaperAccountService
{
    private const decimal FeeRate = 0.001m;
    private const int MaxLeverage = 20;
    private readonly ISqlSugarClient _db;
    private readonly IMarketDataService _market;
    private readonly ILogger<PaperAccountService> _logger;

    public PaperAccountService(ISqlSugarClient db, IMarketDataService market, ILogger<PaperAccountService> logger)
    {
        _db = db;
        _market = market;
        _logger = logger;
    }

    public async Task<PaperAccountEntity> GetOrCreateAsync(CancellationToken ct = default)
    {
        var account = await _db.Queryable<PaperAccountEntity>().FirstAsync(x => x.Id == 1);
        if (account is not null) return account;

        account = new PaperAccountEntity
        {
            Id = 1,
            InitialBalance = 100m,
            Balance = 100m,
            RealizedPnl = 0m,
            UpdatedAt = DateTime.UtcNow
        };
        try
        {
            await _db.Insertable(account).ExecuteCommandAsync();
        }
        catch
        {
            account = await _db.Queryable<PaperAccountEntity>().FirstAsync(x => x.Id == 1) ?? account;
        }
        return account;
    }

    public async Task<PaperSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var account = await GetOrCreateAsync(ct);
        var positions = await _db.Queryable<PositionEntity>()
            .Where(x => x.Mode == TradingMode.Paper && x.Status == PositionStatus.Open)
            .ToListAsync();

        decimal positionValue = 0m;
        decimal usedMargin = 0m;
        decimal unrealized = 0m;
        foreach (var group in positions.GroupBy(x => x.InstId))
        {
            decimal price;
            try
            {
                price = await _market.GetLatestPriceAsync(group.Key, ct);
            }
            catch
            {
                price = group.First().LastPrice > 0m ? group.First().LastPrice : group.First().AvgEntryPrice;
            }

            foreach (var position in group)
            {
                var direction = IsShort(position.Side) ? -1m : 1m;
                var quantity = Math.Abs(position.Quantity);
                usedMargin += position.Margin;
                positionValue += quantity * price;
                unrealized += (price - position.AvgEntryPrice) * quantity * direction;
            }
        }

        var equity = account.Balance + usedMargin + unrealized;
        return new PaperSnapshot
        {
            InitialBalance = account.InitialBalance,
            Equity = Math.Round(equity, 4),
            Available = Math.Round(account.Balance, 4),
            PositionValue = Math.Round(positionValue, 4),
            UsedMargin = Math.Round(usedMargin, 4),
            UnrealizedPnl = Math.Round(unrealized, 4),
            RealizedPnl = Math.Round(account.RealizedPnl, 4),
            ReturnPct = account.InitialBalance <= 0m
                ? 0m
                : Math.Round((equity - account.InitialBalance) / account.InitialBalance * 100m, 4)
        };
    }

    public async Task<(OrderEntity? Order, string? Error)> OpenAsync(
        long taskId, string taskName, string instId, OrderSide side, decimal margin, int leverage,
        decimal marketPrice, decimal slippagePct, string reason, CancellationToken ct = default)
    {
        if (marketPrice <= 0m) return (null, "开仓价格无效");
        if (margin <= 0m) return (null, "保证金必须大于 0");
        leverage = Math.Clamp(leverage, 1, MaxLeverage);
        var isLong = side == OrderSide.Buy;
        var slippage = Math.Clamp(slippagePct, 0m, 5m) / 100m;
        var fillPrice = Math.Round(isLong ? marketPrice * (1m + slippage) : marketPrice * (1m - slippage), 10);
        if (fillPrice <= 0m) return (null, "滑点后成交价无效");

        var notional = margin * leverage;
        var quantity = Math.Round(notional / fillPrice, 8);
        if (quantity <= 0m) return (null, "开仓数量过小，请增加保证金");
        var actualNotional = quantity * fillPrice;
        var fee = Math.Round(actualNotional * FeeRate, 8);
        var required = margin + fee;

        var account = await GetOrCreateAsync(ct);
        if (account.Balance < required)
            return (null, $"模拟盘可用资金不足：需要保证金+手续费 {required:F2} USDT，当前可用 {account.Balance:F2} USDT");

        account.Balance -= required;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(account)
            .UpdateColumns(x => new { x.Balance, x.UpdatedAt })
            .ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        var order = new OrderEntity
        {
            TaskId = taskId,
            TaskName = taskName,
            InstId = instId,
            Mode = TradingMode.Paper,
            Side = side,
            OrderType = "Market",
            Price = Math.Round(marketPrice, 10),
            Quantity = quantity,
            FilledQuantity = quantity,
            AvgFillPrice = fillPrice,
            Fee = fee,
            Leverage = leverage,
            Status = OrderStatus.Filled,
            ClientOrderId = $"paper-{Guid.NewGuid():N}",
            Reason = reason,
            IsPaper = true,
            CreatedAt = now,
            FilledAt = now
        };
        order.Id = await _db.Insertable(order).ExecuteReturnBigIdentityAsync();
        _logger.LogInformation(
            "模拟盘开仓 {InstId} {Side} 数量 {Qty} 成交 {Price} 保证金 {Margin} 杠杆 {Leverage}",
            instId, isLong ? "做多" : "做空", quantity, fillPrice, margin, leverage);
        return (order, null);
    }

    public async Task<(OrderEntity? Order, string? Error)> CloseAsync(
        long taskId, string taskName, PositionEntity position, decimal marketPrice, decimal slippagePct,
        string reason, CancellationToken ct = default)
    {
        if (marketPrice <= 0m) return (null, "平仓价格无效");
        if (position.Quantity <= 0m) return (null, "持仓数量无效");

        var isShort = IsShort(position.Side);
        var slippage = Math.Clamp(slippagePct, 0m, 5m) / 100m;
        var exitPrice = Math.Round(isShort ? marketPrice * (1m + slippage) : marketPrice * (1m - slippage), 10);
        if (exitPrice <= 0m) return (null, "滑点后平仓价无效");

        var quantity = Math.Abs(position.Quantity);
        var direction = isShort ? -1m : 1m;
        var grossPnl = (exitPrice - position.AvgEntryPrice) * quantity * direction;
        var fee = Math.Round(quantity * exitPrice * FeeRate, 8);
        // 逐仓模型下最大亏损不超过该仓保证金，避免价格跳空导致账户出现无上限负余额。
        var netPnl = Math.Round(Math.Max(grossPnl - fee, -position.Margin), 8);
        var refund = position.Margin + netPnl;

        var account = await GetOrCreateAsync(ct);
        account.Balance += refund;
        account.RealizedPnl += netPnl;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(account)
            .UpdateColumns(x => new { x.Balance, x.RealizedPnl, x.UpdatedAt })
            .ExecuteCommandAsync();

        var now = DateTime.UtcNow;
        var order = new OrderEntity
        {
            TaskId = taskId,
            TaskName = taskName,
            InstId = position.InstId,
            Mode = TradingMode.Paper,
            Side = isShort ? OrderSide.Buy : OrderSide.Sell,
            OrderType = "Market",
            Price = Math.Round(marketPrice, 10),
            Quantity = quantity,
            FilledQuantity = quantity,
            AvgFillPrice = exitPrice,
            Fee = fee,
            Leverage = position.Leverage,
            Status = OrderStatus.Filled,
            ClientOrderId = $"paper-{Guid.NewGuid():N}",
            Reason = reason,
            IsPaper = true,
            CreatedAt = now,
            FilledAt = now
        };
        order.Id = await _db.Insertable(order).ExecuteReturnBigIdentityAsync();
        _logger.LogInformation(
            "模拟盘平仓 {InstId} {Side} 数量 {Qty} 成交 {Price} 净盈亏 {Pnl} 返还 {Refund}",
            position.InstId, isShort ? "空头" : "多头", quantity, exitPrice, netPnl, refund);
        return (order, null);
    }

    public async Task ResetAsync(decimal balance, CancellationToken ct = default)
    {
        if (balance <= 0m || balance > 100_000_000m)
            throw new Contracts.BusinessException("重置资金必须大于 0 且不超过 1 亿 USDT");
        if (await _db.Queryable<TaskEntity>().AnyAsync(x => x.Status == TaskStatus.Running))
            throw new Contracts.BusinessException("请先停止所有运行中的任务，再重置模拟盘");

        await _db.Deleteable<PositionEntity>().Where(x => x.Mode == TradingMode.Paper).ExecuteCommandAsync();
        await _db.Deleteable<OrderEntity>().Where(x => x.Mode == TradingMode.Paper).ExecuteCommandAsync();
        await _db.Deleteable<AccountSnapshotEntity>().Where(x => x.Mode == TradingMode.Paper).ExecuteCommandAsync();

        var account = await GetOrCreateAsync(ct);
        account.InitialBalance = balance;
        account.Balance = balance;
        account.RealizedPnl = 0m;
        account.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(account)
            .UpdateColumns(x => new { x.InitialBalance, x.Balance, x.RealizedPnl, x.UpdatedAt })
            .ExecuteCommandAsync();

        await _db.Updateable<TaskEntity>()
            .SetColumns(x => new TaskEntity
            {
                TotalPnl = 0m,
                LastSignal = "FLAT",
                LastProcessedBarTime = null,
                LastError = null,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.Mode == TradingMode.Paper)
            .ExecuteCommandAsync();

        await _db.Insertable(new AccountSnapshotEntity
        {
            Mode = TradingMode.Paper,
            Equity = balance,
            Available = balance,
            PositionValue = 0m,
            UnrealizedPnl = 0m,
            RealizedPnl = 0m,
            CreatedAt = DateTime.UtcNow
        }).ExecuteCommandAsync();
    }

    public async Task RecordSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct);
        var last = await _db.Queryable<AccountSnapshotEntity>()
            .Where(x => x.Mode == TradingMode.Paper)
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync();
        var now = DateTime.UtcNow;
        if (last is not null && (now - last.CreatedAt).TotalMinutes < 3 &&
            Math.Abs(last.Equity - snapshot.Equity) < 0.01m)
            return;

        await _db.Insertable(new AccountSnapshotEntity
        {
            Mode = TradingMode.Paper,
            Equity = snapshot.Equity,
            Available = snapshot.Available,
            PositionValue = snapshot.PositionValue,
            UnrealizedPnl = snapshot.UnrealizedPnl,
            RealizedPnl = snapshot.RealizedPnl,
            CreatedAt = now
        }).ExecuteCommandAsync();
    }

    private static bool IsShort(string? side) =>
        string.Equals(side, "short", StringComparison.OrdinalIgnoreCase);
}
