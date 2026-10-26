using System.Globalization;
using QTS.Contracts;
using QTS.Domain;
using QTS.Services;
using QTS.Infrastructure;
using QTS.Infrastructure.Auth;
using SqlSugar;

namespace QTS.Services;

/// <summary>OKX API 配置持久化：密钥用 DataProtection 加密后存入 qts_app_setting。</summary>
public sealed class OkxConfigService : IOkxConfigService
{
    private const string KeyBaseUrl = "okx.baseUrl";
    private const string KeyApiKey = "okx.apiKey";
    private const string KeySecretKey = "okx.secretKey";
    private const string KeyPassphrase = "okx.passphrase";
    private const string KeySimulated = "okx.simulatedTrading";
    private const string KeyLiveEnabled = "okx.liveTradingEnabled";
    private const string KeyConnected = "okx.connected";
    private const string KeyConnectionError = "okx.connectionError";
    private const string KeySavedAt = "okx.savedAt";

    private static readonly string[] Keys =
    [
        KeyBaseUrl, KeyApiKey, KeySecretKey, KeyPassphrase, KeySimulated,
        KeyLiveEnabled, KeyConnected, KeyConnectionError, KeySavedAt
    ];

    private readonly ISqlSugarClient _db;
    private readonly ISecretProtector _protector;
    private readonly ILogger<OkxConfigService> _logger;

    public OkxConfigService(ISqlSugarClient db, ISecretProtector protector, ILogger<OkxConfigService> logger)
    {
        _db = db;
        _protector = protector;
        _logger = logger;
    }

    public async Task<OkxConfig> GetAsync(CancellationToken ct = default)
    {
        // 配置表很小，直接全量读取，避免 SqlSugar 解析静态字段表达式失败。
        var rows = await _db.Queryable<AppSettingEntity>().ToListAsync();
        var map = rows.ToDictionary(x => x.SettingKey, x => x.SettingValue);

        string Get(string key, string fallback = "") =>
            map.TryGetValue(key, out var value) && value is not null ? value : fallback;

        var config = new OkxConfig
        {
            BaseUrl = Get(KeyBaseUrl, "https://www.okx.com"),
            ApiKey = SafeUnprotect(Get(KeyApiKey)),
            SecretKey = SafeUnprotect(Get(KeySecretKey)),
            Passphrase = SafeUnprotect(Get(KeyPassphrase)),
            SimulatedTrading = ParseBool(Get(KeySimulated, "true"), true),
            LiveTradingEnabled = ParseBool(Get(KeyLiveEnabled, "false")),
            Connected = ParseBool(Get(KeyConnected, "false")),
            ConnectionError = NullIfEmpty(Get(KeyConnectionError))
        };

        var savedAt = Get(KeySavedAt);
        if (DateTime.TryParse(savedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            config.SavedAt = dt;
        return config;
    }

    public async Task<OkxConfig> SaveAsync(OkxConfigUpdate update, CancellationToken ct = default)
    {
        var old = await GetAsync(ct);
        var baseUrl = string.IsNullOrWhiteSpace(update.BaseUrl) ? old.BaseUrl : update.BaseUrl.Trim();
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("OKX API 地址必须以 http:// 或 https:// 开头");

        var apiKey = string.IsNullOrWhiteSpace(update.ApiKey) ? old.ApiKey : update.ApiKey.Trim();
        var secretKey = string.IsNullOrWhiteSpace(update.SecretKey) ? old.SecretKey : update.SecretKey.Trim();
        var passphrase = string.IsNullOrWhiteSpace(update.Passphrase) ? old.Passphrase : update.Passphrase.Trim();

        var hasCredentials = !string.IsNullOrWhiteSpace(apiKey) &&
                             !string.IsNullOrWhiteSpace(secretKey) &&
                             !string.IsNullOrWhiteSpace(passphrase);
        if (update.LiveTradingEnabled && !hasCredentials)
            throw new BusinessException("开启实盘交易前，请先填写完整的 API Key、Secret Key 与 Passphrase");

        var credentialsChanged = apiKey != old.ApiKey || secretKey != old.SecretKey ||
                                 passphrase != old.Passphrase || baseUrl != old.BaseUrl;

        await SetAsync(KeyBaseUrl, baseUrl.TrimEnd('/'));
        if (!string.IsNullOrWhiteSpace(update.ApiKey)) await SetAsync(KeyApiKey, _protector.Protect(apiKey));
        if (!string.IsNullOrWhiteSpace(update.SecretKey)) await SetAsync(KeySecretKey, _protector.Protect(secretKey));
        if (!string.IsNullOrWhiteSpace(update.Passphrase)) await SetAsync(KeyPassphrase, _protector.Protect(passphrase));
        await SetAsync(KeySimulated, update.SimulatedTrading ? "true" : "false");
        await SetAsync(KeyLiveEnabled, update.LiveTradingEnabled ? "true" : "false");
        await SetAsync(KeySavedAt, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        if (credentialsChanged)
        {
            await SetAsync(KeyConnected, "false");
            await SetAsync(KeyConnectionError, "");
        }

        _logger.LogInformation("OKX 配置已保存，实盘开关={Live}", update.LiveTradingEnabled);
        return await GetAsync(ct);
    }

    public async Task MarkTestAsync(bool connected, string? error, CancellationToken ct = default)
    {
        await SetAsync(KeyConnected, connected ? "true" : "false");
        await SetAsync(KeyConnectionError, error ?? "");
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        var config = await GetAsync(ct);
        return config.HasCredentials;
    }

    public async Task<bool> LiveTradingEnabledAsync(CancellationToken ct = default)
    {
        var config = await GetAsync(ct);
        return config.LiveTradingEnabled && config.HasCredentials;
    }

    private async Task SetAsync(string key, string value)
    {
        var count = await _db.Updateable<AppSettingEntity>()
            .SetColumns(x => new AppSettingEntity
            {
                SettingValue = value,
                UpdatedAt = DateTime.UtcNow
            })
            .Where(x => x.SettingKey == key)
            .ExecuteCommandAsync();
        if (count > 0) return;
        try
        {
            await _db.Insertable(new AppSettingEntity
            {
                SettingKey = key,
                SettingValue = value,
                UpdatedAt = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }
        catch
        {
            // 并发插入时回退为更新。
            await _db.Updateable<AppSettingEntity>()
                .SetColumns(x => new AppSettingEntity { SettingValue = value, UpdatedAt = DateTime.UtcNow })
                .Where(x => x.SettingKey == key)
                .ExecuteCommandAsync();
        }
    }

    private string SafeUnprotect(string value)
    {
        try
        {
            return _protector.Unprotect(value);
        }
        catch
        {
            return "";
        }
    }

    private static bool ParseBool(string value, bool fallback = false) =>
        bool.TryParse(value, out var b) ? b : fallback;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
