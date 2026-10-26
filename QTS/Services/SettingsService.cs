using QTS.Contracts;
using QTS.Domain;
using QTS.Services;
using SqlSugar;
using TaskStatus = QTS.Domain.TaskStatus;

namespace QTS.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly ISqlSugarClient _db;
    private readonly IOkxConfigService _okxConfig;
    private readonly IOkxClient _okx;
    private readonly IPaperAccountService _paper;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(
        ISqlSugarClient db,
        IOkxConfigService okxConfig,
        IOkxClient okx,
        IPaperAccountService paper,
        ILogger<SettingsService> logger)
    {
        _db = db;
        _okxConfig = okxConfig;
        _okx = okx;
        _paper = paper;
        _logger = logger;
    }

    public async Task<OkxSettingsDto> GetOkxAsync(CancellationToken ct = default)
    {
        var config = await _okxConfig.GetAsync(ct);
        return Map(config);
    }

    public async Task<OkxSettingsDto> SaveOkxAsync(OkxSettingsUpdateRequest request, CancellationToken ct = default)
    {
        var config = await _okxConfig.SaveAsync(new OkxConfigUpdate
        {
            ApiKey = request.ApiKey,
            SecretKey = request.SecretKey,
            Passphrase = request.Passphrase,
            BaseUrl = request.BaseUrl,
            SimulatedTrading = request.SimulatedTrading,
            LiveTradingEnabled = request.LiveTradingEnabled
        }, ct);
        return Map(config);
    }

    public async Task<OkxConnectionTestResult> TestOkxAsync(CancellationToken ct = default)
    {
        var config = await _okxConfig.GetAsync(ct);
        if (!config.HasCredentials)
        {
            var message = !string.IsNullOrWhiteSpace(config.ApiKey) &&
                          !string.IsNullOrWhiteSpace(config.SecretKey)
                ? "API Key 与 Secret Key 已保存，但仍缺少 Passphrase。请在设置页补全创建 API Key 时设置的 Passphrase 后再测试连接"
                : "请先在系统设置中保存欧易 API Key、Secret Key 与 Passphrase，再测试连接";
            return new OkxConnectionTestResult
            {
                Success = false,
                Message = message
            };
        }

        try
        {
            var result = await _okx.GetAccountAsync(ct);
            if (result.Success && result.Data is not null)
            {
                await _okxConfig.MarkTestAsync(true, null, ct);
                return new OkxConnectionTestResult
                {
                    Success = true,
                    Message = $"连接成功，已读取欧易账户（{result.Data.Currency}）",
                    Account = result.Data
                };
            }

            var error = result.Error ?? "欧易返回空账户数据";
            await _okxConfig.MarkTestAsync(false, error, ct);
            return new OkxConnectionTestResult { Success = false, Message = error };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OKX 连接测试失败");
            await _okxConfig.MarkTestAsync(false, ex.Message, ct);
            return new OkxConnectionTestResult { Success = false, Message = ex.Message };
        }
    }

    public async Task ResetPaperAccountAsync(decimal balance, CancellationToken ct = default)
    {
        await _paper.ResetAsync(balance, ct);
    }

    public async Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken ct = default)
    {
        var info = new SystemInfoDto
        {
            Version = "1.0.0",
            Database = "connected",
            ServerTime = DateTime.UtcNow,
            SyntheticFallbackEnabled = true
        };
        try
        {
            info.MysqlVersion = await _db.Ado.GetStringAsync("SELECT VERSION()") ?? "";
            info.KlineCount = await _db.Queryable<KlineEntity>().CountAsync();
            info.BacktestCount = await _db.Queryable<BacktestEntity>().CountAsync();
            info.TaskCount = await _db.Queryable<TaskEntity>().CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取系统状态失败");
            info.Database = $"error: {ex.Message}";
        }

        try
        {
            var ping = await _okx.PingAsync(ct);
            info.OkxReachable = ping.Success;
        }
        catch
        {
            info.OkxReachable = false;
        }
        return info;
    }

    private static OkxSettingsDto Map(OkxConfig config) => new()
    {
        BaseUrl = config.BaseUrl,
        HasApiKey = !string.IsNullOrWhiteSpace(config.ApiKey),
        ApiKeyMasked = Mask(config.ApiKey),
        HasSecretKey = !string.IsNullOrWhiteSpace(config.SecretKey),
        HasPassphrase = !string.IsNullOrWhiteSpace(config.Passphrase),
        SimulatedTrading = config.SimulatedTrading,
        LiveTradingEnabled = config.LiveTradingEnabled,
        Connected = config.Connected,
        ConnectionError = config.ConnectionError,
        SavedAt = config.SavedAt,
        DataSource = "okx"
    };

    private static string Mask(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Length <= 8) return "****" + value[^2..];
        return value[..4] + "****" + value[^4..];
    }
}
