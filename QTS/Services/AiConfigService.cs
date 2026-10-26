using System.Globalization;
using Microsoft.Extensions.Options;
using QTS.Contracts;
using QTS.Domain;
using QTS.Infrastructure.Auth;
using SqlSugar;

namespace QTS.Services;

/// <summary>
/// AI 引擎配置：appsettings / 环境变量提供默认值，设置页保存的值优先。
/// API Key 使用 DataProtection 加密后写入 qts_app_setting，不在接口中回显。
/// </summary>
public sealed class AiConfigService : IAiConfigService
{
    private const string KeyEnabled = "ai.enabled";
    private const string KeyProvider = "ai.provider";
    private const string KeyBaseUrl = "ai.baseUrl";
    private const string KeyModel = "ai.model";
    private const string KeyApiKey = "ai.apiKey";
    private const string KeyTimeout = "ai.timeoutSeconds";

    private readonly ISqlSugarClient _db;
    private readonly ISecretProtector _protector;
    private readonly AiOptions _defaults;

    public AiConfigService(
        ISqlSugarClient db,
        ISecretProtector protector,
        IOptions<AiOptions> defaults)
    {
        _db = db;
        _protector = protector;
        _defaults = defaults.Value;
    }

    public async Task<AiOptions> GetOptionsAsync(CancellationToken ct = default)
    {
        var rows = await _db.Queryable<AppSettingEntity>().ToListAsync();
        var map = rows.ToDictionary(x => x.SettingKey, x => x.SettingValue);

        string Get(string key, string fallback = "") =>
            map.TryGetValue(key, out var value) && value is not null ? value : fallback;

        var apiKey = SafeUnprotect(Get(KeyApiKey));
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = _defaults.ApiKey;

        return new AiOptions
        {
            Enabled = ParseBool(Get(KeyEnabled), _defaults.Enabled),
            Provider = AiProviders.Normalize(Get(KeyProvider, _defaults.Provider)),
            BaseUrl = NullIfEmpty(Get(KeyBaseUrl)) ?? _defaults.BaseUrl,
            Model = NullIfEmpty(Get(KeyModel)) ?? _defaults.Model,
            ApiKey = apiKey,
            TimeoutSeconds = ParseInt(Get(KeyTimeout), _defaults.TimeoutSeconds, 5, 120)
        };
    }

    public async Task<AiSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var options = await GetOptionsAsync(ct);
        var configured = IsConfigured(options);
        var providerLabel = AiProviders.Label(options.Provider) + " 大模型";
        return new AiSettingsDto
        {
            Enabled = options.Enabled,
            ProviderType = options.Provider,
            BaseUrl = options.BaseUrl,
            Model = options.Model,
            HasApiKey = !string.IsNullOrWhiteSpace(options.ApiKey),
            ApiKeyMasked = Mask(options.ApiKey),
            TimeoutSeconds = options.TimeoutSeconds,
            LlmConfigured = configured,
            Provider = configured ? providerLabel : "本地量化引擎",
            Message = configured
                ? $"已配置 {AiProviders.Label(options.Provider)} 大模型 {options.Model}；调用失败会自动回退本地量化引擎。"
                : "未配置可用的大模型 Key，当前使用离线多因子量化引擎。"
        };
    }

    public async Task<AiSettingsDto> SaveAsync(AiSettingsUpdateRequest request, CancellationToken ct = default)
    {
        var preset = AiProviders.Resolve(request.ProviderType);
        var fallbackBase = string.IsNullOrWhiteSpace(preset.BaseUrl) ? _defaults.BaseUrl : preset.BaseUrl;
        var fallbackModel = string.IsNullOrWhiteSpace(preset.Model) ? _defaults.Model : preset.Model;
        var baseUrl = string.IsNullOrWhiteSpace(request.BaseUrl)
            ? fallbackBase
            : request.BaseUrl.Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("AI Base URL 必须以 http:// 或 https:// 开头");

        var model = string.IsNullOrWhiteSpace(request.Model) ? fallbackModel : request.Model.Trim();
        var timeout = Math.Clamp(request.TimeoutSeconds <= 0 ? 30 : request.TimeoutSeconds, 5, 120);
        var old = await GetOptionsAsync(ct);
        var apiKey = string.IsNullOrWhiteSpace(request.ApiKey) ? old.ApiKey : request.ApiKey.Trim();

        await SetAsync(KeyEnabled, request.Enabled ? "true" : "false");
        await SetAsync(KeyProvider, preset.Key);
        await SetAsync(KeyBaseUrl, baseUrl);
        await SetAsync(KeyModel, model);
        await SetAsync(KeyTimeout, timeout.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
            await SetAsync(KeyApiKey, _protector.Protect(apiKey));

        return await GetAsync(ct);
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
            await _db.Updateable<AppSettingEntity>()
                .SetColumns(x => new AppSettingEntity { SettingValue = value, UpdatedAt = DateTime.UtcNow })
                .Where(x => x.SettingKey == key)
                .ExecuteCommandAsync();
        }
    }

    private string SafeUnprotect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try
        {
            return _protector.Unprotect(value);
        }
        catch
        {
            return "";
        }
    }

    private static bool IsConfigured(AiOptions options) =>
        options.Enabled
        && !string.IsNullOrWhiteSpace(options.BaseUrl)
        && !string.IsNullOrWhiteSpace(options.ApiKey)
        && !string.IsNullOrWhiteSpace(options.Model);

    private static string Mask(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Length <= 8) return "****" + value[^2..];
        return value[..4] + "****" + value[^4..];
    }

    private static bool ParseBool(string value, bool fallback) =>
        bool.TryParse(value, out var b) ? b : fallback;

    private static int ParseInt(string value, int fallback, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? Math.Clamp(n, min, max)
            : Math.Clamp(fallback, min, max);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
