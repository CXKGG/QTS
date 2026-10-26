using System.Text.Json;
using System.Text.Json.Serialization;
using QTS.Domain;
using TaskStatus = QTS.Domain.TaskStatus;

namespace QTS.Contracts;

// ---------------- Auth ----------------
public sealed class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class LoginResponse
{
    public string Token { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public UserDto User { get; set; } = new();
}

public sealed class RegisterRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
}

public sealed class UpdateProfileRequest
{
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
}

public sealed class UserDto
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string Role { get; set; } = "user";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public sealed class ChangePasswordRequest
{
    public string OldPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

// ---------------- Dashboard ----------------
public sealed class EquityPointDto
{
    public long Time { get; set; }
    public decimal Equity { get; set; }
    public decimal? DrawdownPct { get; set; }
}

public sealed class DashboardOverviewDto
{
    public PaperSummaryDto Paper { get; set; } = new();
    public LiveSummaryDto Live { get; set; } = new();
    public TargetProgressDto Target { get; set; } = new();
    public DashboardStatsDto Stats { get; set; } = new();
    public List<EquityPointDto> EquityCurve { get; set; } = [];
    public List<OrderDto> RecentOrders { get; set; } = [];
    public List<PositionDto> TopPositions { get; set; } = [];
}

public sealed class PaperSummaryDto
{
    public decimal InitialBalance { get; set; }
    public decimal Equity { get; set; }
    public decimal Available { get; set; }
    public decimal PositionValue { get; set; }
    public decimal UsedMargin { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal ReturnPct { get; set; }
}

public sealed class LiveSummaryDto
{
    public bool Configured { get; set; }
    public bool Connected { get; set; }
    public decimal TotalEquity { get; set; }
    public decimal AvailableUsdt { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public string? Error { get; set; }
}

public sealed class TargetProgressDto
{
    public decimal Initial { get; set; } = 100m;
    public decimal Target { get; set; } = 10000m;
    public decimal CurrentEquity { get; set; }
    public decimal ProgressPct { get; set; }
}

public sealed class DashboardStatsDto
{
    public int RunningTasks { get; set; }
    public int TotalTasks { get; set; }
    public int OpenPositions { get; set; }
    public decimal TodayPnl { get; set; }
    public decimal TotalPnl { get; set; }
    public int TodayTrades { get; set; }
    public decimal WinRate { get; set; }
}

// ---------------- Market ----------------
public sealed class InstrumentDto
{
    public string InstId { get; set; } = "";
    public string Base { get; set; } = "";
    public string Quote { get; set; } = "USDT";
    public string Name { get; set; } = "";
}

public sealed class CandleDto
{
    public long Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

public sealed class TickerDto
{
    public string InstId { get; set; } = "";
    public decimal Last { get; set; }
    public decimal Change24hPct { get; set; }
    public decimal High24h { get; set; }
    public decimal Low24h { get; set; }
    public decimal Volume24h { get; set; }
    public long Time { get; set; }
}

public sealed class MarketSyncRequest
{
    public string InstId { get; set; } = "BTC-USDT";
    public string Bar { get; set; } = "1H";
    public int Days { get; set; } = 30;
}

public sealed class MarketSyncResult
{
    public string InstId { get; set; } = "";
    public string Bar { get; set; } = "";
    public int Count { get; set; }
    public string Source { get; set; } = "okx";
    public string? Message { get; set; }
}

public sealed class MarketSyncAllRequest
{
    public string Bar { get; set; } = "1H";
    public int Days { get; set; } = 30;
}

public sealed class MarketSyncAllResult
{
    public string Bar { get; set; } = "1H";
    public int Days { get; set; }
    public int InstrumentCount { get; set; }
    public int TotalCount { get; set; }
    public string Source { get; set; } = "okx";
    public string? Message { get; set; }
    public List<MarketSyncResult> Items { get; set; } = [];
}

// ---------------- Strategies ----------------
public sealed class StrategyDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public List<StrategyParamDto> ParamsSchema { get; set; } = [];
    public Dictionary<string, object?> DefaultParams { get; set; } = [];
}

public sealed class StrategyParamDto
{
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "int";
    public object? Default { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public decimal? Step { get; set; }
    public List<StrategyOptionDto>? Options { get; set; }
}

public sealed class StrategyOptionDto
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

// ---------------- Backtest ----------------
public sealed class BacktestCreateRequest
{
    public string Name { get; set; } = "";
    public string StrategyCode { get; set; } = "";
    public string InstId { get; set; } = "BTC-USDT";
    public string Bar { get; set; } = "1H";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public decimal InitialCapital { get; set; } = 100m;
    public Dictionary<string, JsonElement>? Params { get; set; }
    public decimal FeeRatePct { get; set; } = 0.1m;
    public decimal SlippagePct { get; set; } = 0.05m;
    public decimal PositionSizePct { get; set; } = 95m;
    public int Leverage { get; set; } = 1;
    public decimal StopLossPct { get; set; }
    public decimal TakeProfitPct { get; set; }
    public bool PreferLocalData { get; set; }
}

public sealed class BacktestSummaryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string StrategyCode { get; set; } = "";
    public string StrategyName { get; set; } = "";
    public string InstId { get; set; } = "";
    public string Bar { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public decimal InitialCapital { get; set; }
    public decimal FinalEquity { get; set; }
    public decimal TotalReturnPct { get; set; }
    public decimal MaxDrawdownPct { get; set; }
    public decimal Sharpe { get; set; }
    public decimal WinRatePct { get; set; }
    public int TradeCount { get; set; }
    public string Status { get; set; } = "";
    public string DataSource { get; set; } = "okx";
    public decimal FeeRatePct { get; set; }
    public decimal SlippagePct { get; set; }
    public decimal PositionSizePct { get; set; }
    public int Leverage { get; set; } = 1;
    public decimal StopLossPct { get; set; }
    public decimal TakeProfitPct { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal Fees { get; set; }
    public decimal BuyAndHoldReturnPct { get; set; }
    public long DurationMs { get; set; }
    public string? Error { get; set; }
    public object? Params { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class BacktestDetailDto
{
    public BacktestSummaryDto Summary { get; set; } = new();
    public List<EquityPointDto> EquityCurve { get; set; } = [];
    public List<BacktestTradeDto> Trades { get; set; } = [];
    public List<MonthlyReturnDto> MonthlyReturns { get; set; } = [];
    public BenchmarkDto Benchmark { get; set; } = new();
}

public sealed class BacktestTradeDto
{
    public long Id { get; set; }
    public DateTime EntryTime { get; set; }
    public DateTime ExitTime { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal Fee { get; set; }
    public decimal Pnl { get; set; }
    public decimal PnlPct { get; set; }
    public string Side { get; set; } = "long";
    public decimal Margin { get; set; }
    public string ExitReason { get; set; } = "";
    public int HoldingBars { get; set; }
}

public sealed class MonthlyReturnDto
{
    public string Month { get; set; } = "";
    public decimal ReturnPct { get; set; }
}

public sealed class BenchmarkDto
{
    public decimal BuyAndHoldReturnPct { get; set; }
}

public sealed class BacktestRunResult
{
    public long Id { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public string DataSource { get; set; } = "okx";
    public BacktestSummaryDto? Summary { get; set; }

    /// <summary>契约示例中的别名，与 Summary 同值。</summary>
    [System.Text.Json.Serialization.JsonPropertyName("metrics")]
    public BacktestSummaryDto? Metrics => Summary;
}

// ---------------- Tasks ----------------
public sealed class TaskDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string StrategyCode { get; set; } = "";
    public string StrategyName { get; set; } = "";
    public string InstId { get; set; } = "";
    public string Bar { get; set; } = "";
    public TradingMode Mode { get; set; }
    public TaskStatus Status { get; set; }
    public decimal AllocatedCapital { get; set; }
    public int Leverage { get; set; } = 1;
    public decimal SlippagePct { get; set; } = 0.05m;
    public JsonElement? Params { get; set; }
    public TaskRiskDto Risk { get; set; } = new();
    public decimal TotalPnl { get; set; }
    public string LastSignal { get; set; } = "FLAT";
    public DateTime? LastRunAt { get; set; }
    public string? LastError { get; set; }
    public decimal PositionQty { get; set; }
    public decimal PositionAvgPrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class TaskCreateRequest
{
    public string Name { get; set; } = "";
    public string StrategyCode { get; set; } = "";
    public string InstId { get; set; } = "BTC-USDT";
    public string Bar { get; set; } = "1H";
    public TradingMode Mode { get; set; } = TradingMode.Paper;
    public decimal AllocatedCapital { get; set; } = 100m;
    public int Leverage { get; set; } = 1;
    public decimal SlippagePct { get; set; } = 0.05m;
    public Dictionary<string, JsonElement>? Params { get; set; }
    public TaskRiskDto Risk { get; set; } = new();
    public bool StartImmediately { get; set; } = true;
}

public sealed class TaskRiskDto
{
    public decimal StopLossPct { get; set; } = 5m;
    public decimal TakeProfitPct { get; set; } = 15m;
    public decimal MaxPositionPct { get; set; } = 95m;
}

// ---------------- Orders / positions ----------------
public sealed class OrderDto
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public string TaskName { get; set; } = "";
    public string InstId { get; set; } = "";
    public TradingMode Mode { get; set; }
    public OrderSide Side { get; set; }
    public string OrderType { get; set; } = "Market";
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal AvgFillPrice { get; set; }
    public decimal Fee { get; set; }
    public int Leverage { get; set; } = 1;
    public OrderStatus Status { get; set; }
    public string? OkxOrderId { get; set; }
    public string? Error { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? FilledAt { get; set; }
}

public sealed class PositionDto
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public string TaskName { get; set; } = "";
    public string InstId { get; set; } = "";
    public TradingMode Mode { get; set; }
    public string Side { get; set; } = "long";
    public decimal Quantity { get; set; }
    public decimal AvgEntryPrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal UnrealizedPnlPct { get; set; }
    public decimal Margin { get; set; }
    public int Leverage { get; set; } = 1;
    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
    public DateTime OpenedAt { get; set; }
}

// ---------------- Manual trading ----------------
public sealed class TradeAccountDto
{
    public bool Configured { get; set; }
    public bool Connected { get; set; }
    public bool Simulated { get; set; } = true;
    public string AccountType { get; set; } = "OKX API 账户";
    public string Environment { get; set; } = "模拟环境";
    public decimal TotalEquity { get; set; }
    public decimal Available { get; set; }
    public decimal UsedMargin { get; set; }
    public decimal PositionValue { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal RealizedPnl { get; set; }
    public string Currency { get; set; } = "USDT";
    public string? Error { get; set; }
}

public sealed class TradeOverviewDto
{
    public TradeAccountDto Account { get; set; } = new();
    public PaperSummaryDto Paper { get; set; } = new();
    public List<InstrumentDto> Instruments { get; set; } = [];
    public TickerDto? Ticker { get; set; }
    public List<PositionDto> Positions { get; set; } = [];
    public List<TaskDto> Tasks { get; set; } = [];
}

public sealed class ManualTradeRequest
{
    public string InstId { get; set; } = "BTC-USDT";
    /// <summary>long / short。</summary>
    public string Direction { get; set; } = "long";
    /// <summary>保证金（USDT）。名义价值 = 保证金 × 杠杆。</summary>
    public decimal Margin { get; set; } = 10m;
    public int Leverage { get; set; } = 1;
    public decimal SlippagePct { get; set; } = 0.05m;
    public decimal StopLossPct { get; set; }
    public decimal TakeProfitPct { get; set; }
}

public sealed class ManualCloseRequest
{
    public long PositionId { get; set; }
    public decimal SlippagePct { get; set; } = 0.05m;
}

public sealed class ManualTradeResult
{
    public OrderDto? Order { get; set; }
    public PositionDto? Position { get; set; }
    public TradeAccountDto? Account { get; set; }
    public string Message { get; set; } = "";
}

// ---------------- AI decision ----------------
public sealed class AiStatusDto
{
    /// <summary>local / llm</summary>
    public string Mode { get; set; } = "local";
    /// <summary>openai / deepseek / moonshot / qwen / zhipu / custom</summary>
    public string ProviderType { get; set; } = "local";
    public string Provider { get; set; } = "本地量化引擎";
    public string Model { get; set; } = "qts-local-v1";
    public bool LlmConfigured { get; set; }
    public string Message { get; set; } = "";
}

public sealed class AiSettingsDto
{
    public bool Enabled { get; set; }
    /// <summary>openai / deepseek / moonshot / qwen / zhipu / custom</summary>
    public string ProviderType { get; set; } = "openai";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    public bool HasApiKey { get; set; }
    public string ApiKeyMasked { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool LlmConfigured { get; set; }
    public string Provider { get; set; } = "本地量化引擎";
    public string Message { get; set; } = "";
}

public sealed class AiSettingsUpdateRequest
{
    public bool Enabled { get; set; }
    /// <summary>openai / deepseek / moonshot / qwen / zhipu / custom</summary>
    public string ProviderType { get; set; } = "openai";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class AiAnalysisRequest
{
    public string InstId { get; set; } = "BTC-USDT";
    public string Bar { get; set; } = "4H";
    /// <summary>conservative / balanced / aggressive</summary>
    public string RiskProfile { get; set; } = "balanced";
    public int LookbackDays { get; set; } = 180;
    public bool AllowShort { get; set; } = true;
}

public sealed class AiTrendDto
{
    public string Direction { get; set; } = "sideways";
    public string Label { get; set; } = "震荡";
    public int Score { get; set; }
    public int Strength { get; set; }
}

public sealed class AiIndicatorsDto
{
    public decimal Ema20 { get; set; }
    public decimal Ema60 { get; set; }
    public decimal Rsi14 { get; set; }
    public decimal Macd { get; set; }
    public decimal MacdSignal { get; set; }
    public decimal AtrPct { get; set; }
    public decimal VolumeRatio { get; set; }
    public decimal Support { get; set; }
    public decimal Resistance { get; set; }
}

public sealed class AiSuggestionDto
{
    public string StrategyCode { get; set; } = "";
    public string StrategyName { get; set; } = "";
    /// <summary>long / short / both</summary>
    public string DirectionBias { get; set; } = "both";
    public int Confidence { get; set; }
    public int Leverage { get; set; } = 1;
    public decimal SlippagePct { get; set; } = 0.05m;
    public Dictionary<string, object?> Params { get; set; } = [];
    public TaskRiskDto Risk { get; set; } = new();
    public decimal CapitalPct { get; set; } = 30m;
    public string Summary { get; set; } = "";
    public List<string> Rationale { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class AiAnalysisResultDto
{
    public string InstId { get; set; } = "";
    public string Bar { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string Provider { get; set; } = "本地量化引擎";
    public string Model { get; set; } = "qts-local-v1";
    public string DataSource { get; set; } = "synthetic";
    public string? Warning { get; set; }
    public int Candles { get; set; }
    public decimal LastPrice { get; set; }
    public decimal ChangePct { get; set; }
    public AiTrendDto Trend { get; set; } = new();
    public AiIndicatorsDto Indicators { get; set; } = new();
    public AiSuggestionDto Suggestion { get; set; } = new();
}

// ---------------- Settings ----------------
public sealed class OkxSettingsDto
{
    public string BaseUrl { get; set; } = "https://www.okx.com";
    public bool HasApiKey { get; set; }
    public string ApiKeyMasked { get; set; } = "";
    public bool HasSecretKey { get; set; }
    public bool HasPassphrase { get; set; }
    public bool SimulatedTrading { get; set; } = true;
    public bool LiveTradingEnabled { get; set; }
    public bool Connected { get; set; }
    public string? ConnectionError { get; set; }
    public DateTime? SavedAt { get; set; }
    public string DataSource { get; set; } = "okx";
}

public sealed class OkxSettingsUpdateRequest
{
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Passphrase { get; set; }
    public string? BaseUrl { get; set; }
    public bool SimulatedTrading { get; set; } = true;
    public bool LiveTradingEnabled { get; set; }
}

public sealed class OkxConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public OkxAccountSummaryDto? Account { get; set; }
}

public sealed class OkxAccountSummaryDto
{
    public decimal TotalEquity { get; set; }
    public decimal AvailableUsdt { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public string Currency { get; set; } = "USDT";
}

public sealed class PaperAccountResetRequest
{
    public decimal Balance { get; set; } = 100m;
}

public sealed class SystemInfoDto
{
    public string Version { get; set; } = "1.0.0";
    public string Database { get; set; } = "unknown";
    public string MysqlVersion { get; set; } = "";
    public long KlineCount { get; set; }
    public long BacktestCount { get; set; }
    public long TaskCount { get; set; }
    public DateTime ServerTime { get; set; } = DateTime.UtcNow;
    public bool OkxReachable { get; set; }
    public bool SyntheticFallbackEnabled { get; set; } = true;
}
