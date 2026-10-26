using SqlSugar;

namespace QTS.Domain;

public abstract class BaseEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }
}

[SugarTable("qts_user")]
public sealed class UserEntity : BaseEntity
{
    [SugarColumn(Length = 64, IsNullable = false)]
    public string Username { get; set; } = "";

    [SugarColumn(Length = 512, IsNullable = false)]
    public string PasswordHash { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = false)]
    public string DisplayName { get; set; } = "";

    [SugarColumn(Length = 128, IsNullable = true)]
    public string? Email { get; set; }

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Role { get; set; } = "user";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)]
    public DateTime? LastLoginAt { get; set; }
    [SugarColumn(IsNullable = true)]
    public DateTime? UpdatedAt { get; set; }
}

[SugarTable("qts_strategy")]
public sealed class StrategyEntity : BaseEntity
{
    [SugarColumn(Length = 64, IsNullable = false)]
    public string Code { get; set; } = "";

    [SugarColumn(Length = 128, IsNullable = false)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 32, IsNullable = false)]
    public string Category { get; set; } = "";

    [SugarColumn(Length = 1024, IsNullable = false)]
    public string Description { get; set; } = "";

    [SugarColumn(ColumnDataType = "longtext", IsNullable = false)]
    public string ParamsSchemaJson { get; set; } = "[]";

    [SugarColumn(ColumnDataType = "longtext", IsNullable = false)]
    public string DefaultParamsJson { get; set; } = "{}";

    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [SugarColumn(ColumnDataType = "longtext", IsNullable = true)]
    public string? Remark { get; set; }
}

[SugarTable("qts_kline")]
public sealed class KlineEntity : BaseEntity
{
    [SugarColumn(Length = 32, IsNullable = false)]
    public string InstId { get; set; } = "";

    [SugarColumn(Length = 8, IsNullable = false)]
    public string Bar { get; set; } = "1H";

    public DateTime OpenTime { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Open { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal High { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Low { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Close { get; set; }

    [SugarColumn(ColumnDataType = "decimal(38,10)")]
    public decimal Volume { get; set; }

    [SugarColumn(Length = 16, IsNullable = false)]
    public string Source { get; set; } = "okx";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[SugarTable("qts_backtest")]
public sealed class BacktestEntity : BaseEntity
{
    [SugarColumn(Length = 128, IsNullable = false)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = false)]
    public string StrategyCode { get; set; } = "";

    [SugarColumn(Length = 32, IsNullable = false)]
    public string InstId { get; set; } = "BTC-USDT";

    [SugarColumn(Length = 8, IsNullable = false)]
    public string Bar { get; set; } = "1H";

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal InitialCapital { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal FeeRatePct { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal SlippagePct { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal PositionSizePct { get; set; }

    /// <summary>回测杠杆倍数（1 表示无杠杆，仅影响保证金与仓位规模）。</summary>
    public int Leverage { get; set; } = 1;

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal StopLossPct { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal TakeProfitPct { get; set; }

    [SugarColumn(ColumnDataType = "longtext", IsNullable = false)]
    public string ParamsJson { get; set; } = "{}";

    [SugarColumn(Length = 16, IsNullable = false)]
    public string Status { get; set; } = "Running";

    [SugarColumn(Length = 16, IsNullable = false)]
    public string DataSource { get; set; } = "okx";

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal FinalEquity { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal TotalReturnPct { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal MaxDrawdownPct { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Sharpe { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal WinRatePct { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal ProfitFactor { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal BuyAndHoldReturnPct { get; set; }

    public int TradeCount { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Fees { get; set; }

    public long DurationMs { get; set; }

    [SugarColumn(Length = 2000, IsNullable = true)]
    public string? Error { get; set; }

    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[SugarTable("qts_backtest_equity")]
public sealed class BacktestEquityEntity : BaseEntity
{
    public long BacktestId { get; set; }
    public DateTime Time { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Equity { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal DrawdownPct { get; set; }
}

[SugarTable("qts_backtest_trade")]
public sealed class BacktestTradeEntity : BaseEntity
{
    public long BacktestId { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal EntryPrice { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal ExitPrice { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Quantity { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Fee { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Pnl { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal PnlPct { get; set; }

    /// <summary>long / short。</summary>
    [SugarColumn(Length = 8)]
    public string Side { get; set; } = "long";

    /// <summary>本回合占用的保证金（含杠杆后）。</summary>
    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Margin { get; set; }

    [SugarColumn(Length = 64)]
    public string ExitReason { get; set; } = "";

    public int HoldingBars { get; set; }
}

[SugarTable("qts_task")]
public sealed class TaskEntity : BaseEntity
{
    [SugarColumn(Length = 128, IsNullable = false)]
    public string Name { get; set; } = "";

    [SugarColumn(Length = 64, IsNullable = false)]
    public string StrategyCode { get; set; } = "";

    [SugarColumn(Length = 32, IsNullable = false)]
    public string InstId { get; set; } = "BTC-USDT";

    [SugarColumn(Length = 8, IsNullable = false)]
    public string Bar { get; set; } = "1H";

    public TradingMode Mode { get; set; } = TradingMode.Paper;
    public TaskStatus Status { get; set; } = TaskStatus.Stopped;

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal AllocatedCapital { get; set; }

    /// <summary>交易杠杆倍数，1 表示无杠杆。模拟盘按保证金占用计算；实盘 &gt;1 时走 USDT 永续逐仓。</summary>
    public int Leverage { get; set; } = 1;

    /// <summary>单边滑点百分比（0.05 表示 0.05%）。</summary>
    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal SlippagePct { get; set; } = 0.05m;

    [SugarColumn(ColumnDataType = "longtext", IsNullable = false)]
    public string ParamsJson { get; set; } = "{}";

    [SugarColumn(ColumnDataType = "longtext", IsNullable = false)]
    public string RiskJson { get; set; } = "{}";

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal TotalPnl { get; set; }

    [SugarColumn(Length = 16)]
    public string LastSignal { get; set; } = "FLAT";

    [SugarColumn(IsNullable = true)]
    public DateTime? LastRunAt { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? LastProcessedBarTime { get; set; }

    [SugarColumn(Length = 2000, IsNullable = true)]
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long UserId { get; set; }
}

[SugarTable("qts_position")]
public sealed class PositionEntity : BaseEntity
{
    public long TaskId { get; set; }

    [SugarColumn(Length = 32)]
    public string InstId { get; set; } = "";

    public TradingMode Mode { get; set; } = TradingMode.Paper;

    [SugarColumn(Length = 8)]
    public string Side { get; set; } = "long";

    [SugarColumn(ColumnDataType = "decimal(38,12)")]
    public decimal Quantity { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal AvgEntryPrice { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal LastPrice { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal UnrealizedPnl { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal RealizedPnl { get; set; }

    /// <summary>开仓保证金（USDT），到价平仓后返还。</summary>
    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Margin { get; set; }

    /// <summary>杠杆倍数，1 表示无杠杆。</summary>
    public int Leverage { get; set; } = 1;

    /// <summary>合约面值（每张对应的基础币数量）；现货固定为 1。</summary>
    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal CtVal { get; set; } = 1m;

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal StopLoss { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal TakeProfit { get; set; }

    public PositionStatus Status { get; set; } = PositionStatus.Open;
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)]
    public DateTime? ClosedAt { get; set; }
}

[SugarTable("qts_order")]
public sealed class OrderEntity : BaseEntity
{
    public long TaskId { get; set; }

    [SugarColumn(Length = 128)]
    public string TaskName { get; set; } = "";

    [SugarColumn(Length = 32)]
    public string InstId { get; set; } = "";

    public TradingMode Mode { get; set; } = TradingMode.Paper;
    public OrderSide Side { get; set; } = OrderSide.Buy;

    [SugarColumn(Length = 16)]
    public string OrderType { get; set; } = "Market";

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Price { get; set; }

    [SugarColumn(ColumnDataType = "decimal(38,12)")]
    public decimal Quantity { get; set; }

    [SugarColumn(ColumnDataType = "decimal(38,12)")]
    public decimal FilledQuantity { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal AvgFillPrice { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Fee { get; set; }

    /// <summary>下单时的杠杆倍数，仅用于展示与审计。</summary>
    public int Leverage { get; set; } = 1;

    /// <summary>合约面值（每张对应的基础币数量）；现货固定为 1。</summary>
    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal CtVal { get; set; } = 1m;

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    [SugarColumn(Length = 64, IsNullable = true)]
    public string? OkxOrderId { get; set; }

    [SugarColumn(Length = 64)]
    public string ClientOrderId { get; set; } = "";

    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? Error { get; set; }

    [SugarColumn(Length = 256)]
    public string Reason { get; set; } = "";

    public bool IsPaper { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [SugarColumn(IsNullable = true)]
    public DateTime? FilledAt { get; set; }
}

[SugarTable("qts_paper_account")]
public sealed class PaperAccountEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
    public long Id { get; set; } = 1;

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal InitialBalance { get; set; } = 100m;

    /// <summary>可用现金（USDT）。</summary>
    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Balance { get; set; } = 100m;

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal RealizedPnl { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[SugarTable("qts_account_snapshot")]
public sealed class AccountSnapshotEntity : BaseEntity
{
    public TradingMode Mode { get; set; } = TradingMode.Paper;

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Equity { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal Available { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal PositionValue { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal UnrealizedPnl { get; set; }

    [SugarColumn(ColumnDataType = "decimal(28,10)")]
    public decimal RealizedPnl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[SugarTable("qts_app_setting")]
public sealed class AppSettingEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 64, IsIdentity = false)]
    public string SettingKey { get; set; } = "";

    [SugarColumn(ColumnDataType = "longtext", IsNullable = false)]
    public string SettingValue { get; set; } = "";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[SugarTable("qts_audit_log")]
public sealed class AuditLogEntity : BaseEntity
{
    [SugarColumn(Length = 64)]
    public string Action { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string Level { get; set; } = "Info";

    [SugarColumn(ColumnDataType = "text")]
    public string Message { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
