using QTS.Contracts;
using QTS.Domain;

namespace QTS.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<LoginResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<UserDto?> GetUserAsync(long userId, CancellationToken ct = default);
    Task<UserDto> UpdateProfileAsync(long userId, UpdateProfileRequest request, CancellationToken ct = default);
    Task<bool> ChangePasswordAsync(long userId, string oldPassword, string newPassword, CancellationToken ct = default);
}

public interface IStrategyRegistry
{
    IReadOnlyList<IStrategy> All { get; }
    IStrategy? Get(string code);
}

/// <summary>策略只输出目标仓位（1=持多，0=空仓，-1=持空），不直接下单。</summary>
public interface IStrategy
{
    string Code { get; }
    string Name { get; }
    string Category { get; }
    string Description { get; }
    IReadOnlyList<StrategyParamDescriptor> ParamsSchema { get; }
    IReadOnlyDictionary<string, object?> DefaultParams { get; }
    IReadOnlyList<StrategySignal> Evaluate(IReadOnlyList<CandlePoint> candles, IReadOnlyDictionary<string, object?> parameters);
}

public sealed class MarketCandleSet
{
    public List<CandlePoint> Candles { get; init; } = [];
    public string Source { get; init; } = "synthetic";
    public string? Warning { get; init; }
}

public interface IMarketDataService
{
    IReadOnlyList<MarketInstrument> GetInstruments();
    Task<MarketCandleSet> EnsureCandlesAsync(string instId, string bar, DateTime startUtc, DateTime endUtc, bool preferLocal, CancellationToken ct = default);
    Task<MarketSyncResult> SyncAsync(string instId, string bar, int days, CancellationToken ct = default);
    Task<TickerDto> GetTickerAsync(string instId, CancellationToken ct = default);
    Task<decimal> GetLatestPriceAsync(string instId, CancellationToken ct = default);
    Task<List<CandleDto>> GetRecentCandlesAsync(string instId, string bar, int limit, CancellationToken ct = default);
}

public interface IAiAnalysisService
{
    Task<AiStatusDto> GetStatusAsync(CancellationToken ct = default);
    Task<AiAnalysisResultDto> AnalyzeAsync(AiAnalysisRequest request, CancellationToken ct = default);
}

public interface IAiConfigService
{
    Task<AiOptions> GetOptionsAsync(CancellationToken ct = default);
    Task<AiSettingsDto> GetAsync(CancellationToken ct = default);
    Task<AiSettingsDto> SaveAsync(AiSettingsUpdateRequest request, CancellationToken ct = default);
}

public interface IBacktestService
{
    Task<BacktestRunResult> RunAsync(BacktestCreateRequest request, long userId, CancellationToken ct = default);
    Task<PagedResult<BacktestSummaryDto>> ListAsync(int page, int pageSize, string? strategyCode, string? instId, CancellationToken ct = default);
    Task<BacktestDetailDto?> GetAsync(long id, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

public interface ITaskService
{
    Task<PagedResult<TaskDto>> ListAsync(int page, int pageSize, string? status, CancellationToken ct = default);
    Task<TaskDto?> GetAsync(long id, CancellationToken ct = default);
    Task<TaskDto> CreateAsync(TaskCreateRequest request, long userId, CancellationToken ct = default);
    Task<TaskDto> UpdateAsync(long id, TaskCreateRequest request, CancellationToken ct = default);
    Task<TaskDto> StartAsync(long id, CancellationToken ct = default);
    Task<TaskDto> StopAsync(long id, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

public interface ITradingEngine
{
    Task ExecuteTaskAsync(long taskId, CancellationToken ct = default);
    Task RefreshPaperPositionsAsync(CancellationToken ct = default);
    Task<bool> CloseTaskPositionAsync(long taskId, CancellationToken ct = default);
}

public sealed class PaperSnapshot
{
    public decimal InitialBalance { get; init; }
    public decimal Equity { get; init; }
    public decimal Available { get; init; }
    public decimal PositionValue { get; init; }
    public decimal UsedMargin { get; init; }
    public decimal UnrealizedPnl { get; init; }
    public decimal RealizedPnl { get; init; }
    public decimal ReturnPct { get; init; }
}

public interface IPaperAccountService
{
    Task<PaperSnapshot> GetSnapshotAsync(CancellationToken ct = default);
    Task<PaperAccountEntity> GetOrCreateAsync(CancellationToken ct = default);

    /// <summary>模拟盘开仓：冻结保证金并生成成交订单（价格已含滑点），支持多/空与杠杆。</summary>
    Task<(OrderEntity? Order, string? Error)> OpenAsync(
        long taskId, string taskName, string instId, OrderSide side, decimal margin, int leverage,
        decimal marketPrice, decimal slippagePct, string reason, CancellationToken ct = default);

    /// <summary>模拟盘平仓：释放保证金并按方向结算盈亏，生成反向成交订单。</summary>
    Task<(OrderEntity? Order, string? Error)> CloseAsync(
        long taskId, string taskName, PositionEntity position, decimal marketPrice, decimal slippagePct,
        string reason, CancellationToken ct = default);

    Task ResetAsync(decimal balance, CancellationToken ct = default);
    Task RecordSnapshotAsync(CancellationToken ct = default);
}

public interface IManualTradeService
{
    Task<TradeOverviewDto> GetOverviewAsync(string? instId, CancellationToken ct = default);
    Task<ManualTradeResult> OpenAsync(ManualTradeRequest request, CancellationToken ct = default);
    Task<ManualTradeResult> CloseAsync(ManualCloseRequest request, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardOverviewDto> GetOverviewAsync(CancellationToken ct = default);
    Task<List<EquityPointDto>> GetEquityCurveAsync(string range, CancellationToken ct = default);
}

public interface IOkxConfigService
{
    Task<OkxConfig> GetAsync(CancellationToken ct = default);
    Task<OkxConfig> SaveAsync(OkxConfigUpdate update, CancellationToken ct = default);
    Task MarkTestAsync(bool connected, string? error, CancellationToken ct = default);
    Task<bool> IsConfiguredAsync(CancellationToken ct = default);
    Task<bool> LiveTradingEnabledAsync(CancellationToken ct = default);
}

public sealed class OkxConfig
{
    public string BaseUrl { get; set; } = "https://www.okx.com";
    public string ApiKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Passphrase { get; set; } = "";
    public bool SimulatedTrading { get; set; } = true;
    public bool LiveTradingEnabled { get; set; }
    public bool Connected { get; set; }
    public string? ConnectionError { get; set; }
    public DateTime? SavedAt { get; set; }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(ApiKey)
                                  && !string.IsNullOrWhiteSpace(SecretKey)
                                  && !string.IsNullOrWhiteSpace(Passphrase);
}

public sealed class OkxConfigUpdate
{
    public string? ApiKey { get; set; }
    public string? SecretKey { get; set; }
    public string? Passphrase { get; set; }
    public string? BaseUrl { get; set; }
    public bool SimulatedTrading { get; set; } = true;
    public bool LiveTradingEnabled { get; set; }
}

public sealed class OkxResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }

    public static OkxResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static OkxResult<T> Fail(string error) => new() { Success = false, Error = error };
}

public sealed class OkxOrderFill
{
    public string OrderId { get; init; } = "";
    public decimal FilledQuantity { get; init; }
    public decimal AvgPrice { get; init; }
    public decimal Fee { get; init; }
    public string State { get; init; } = "filled";
}

/// <summary>OKX 永续合约规格（用于把保证金/币数量换算成张数）。</summary>
public sealed class OkxSwapInstrument
{
    public string InstId { get; init; } = "";
    public decimal CtVal { get; init; } = 1m;
    public decimal LotSz { get; init; } = 1m;
    public decimal MinSz { get; init; } = 1m;
    public decimal TickSz { get; init; } = 0.01m;
}

/// <summary>OKX 账户持仓模式；仓位模式下下单必须带 posSide。</summary>
public sealed class OkxAccountConfig
{
    public string PositionMode { get; init; } = "net_mode";

    public bool IsLongShortMode =>
        string.Equals(PositionMode, "long_short_mode", StringComparison.OrdinalIgnoreCase);
}

public interface IOkxClient
{
    Task<OkxResult<List<CandlePoint>>> GetCandlesAsync(string instId, string bar, DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
    Task<OkxResult<TickerDto>> GetTickerAsync(string instId, CancellationToken ct = default);
    Task<OkxResult<OkxAccountSummaryDto>> GetAccountAsync(CancellationToken ct = default);
    Task<OkxResult<OkxOrderFill>> PlaceMarketOrderAsync(string instId, OrderSide side, decimal baseQuantity, string clientOrderId, CancellationToken ct = default);
    Task<OkxResult<OkxAccountConfig>> GetAccountConfigAsync(CancellationToken ct = default);
    Task<OkxResult<OkxSwapInstrument>> GetSwapInstrumentAsync(string instId, CancellationToken ct = default);
    Task<OkxResult<bool>> SetLeverageAsync(string instId, int leverage, string? posSide, CancellationToken ct = default);
    Task<OkxResult<OkxOrderFill>> PlaceSwapMarketOrderAsync(
        string instId,
        OrderSide side,
        string? posSide,
        decimal contracts,
        bool reduceOnly,
        string clientOrderId,
        CancellationToken ct = default);
    Task<OkxResult<bool>> PingAsync(CancellationToken ct = default);
}

public interface ISettingsService
{
    Task<OkxSettingsDto> GetOkxAsync(CancellationToken ct = default);
    Task<OkxSettingsDto> SaveOkxAsync(OkxSettingsUpdateRequest request, CancellationToken ct = default);
    Task<OkxConnectionTestResult> TestOkxAsync(CancellationToken ct = default);
    Task ResetPaperAccountAsync(decimal balance, CancellationToken ct = default);
    Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken ct = default);
}

public interface ITokenService
{
    string CreateToken(UserEntity user, out DateTime expiresAt);
    bool TryValidateToken(string token, out long userId, out string username);
}

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}
