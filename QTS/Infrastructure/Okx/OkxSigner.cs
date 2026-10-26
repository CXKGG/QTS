using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using QTS.Services;

namespace QTS.Infrastructure.Okx;

/// <summary>
/// 欧易（OKX）V5 REST 鉴权工具。
/// 规则：OK-ACCESS-SIGN = Base64(HMAC-SHA256(secretKey, timestamp + method + requestPathWithQuery + body))；
/// OK-ACCESS-TIMESTAMP 使用 UTC ISO8601 毫秒（如 2020-12-08T09:08:57.715Z）；
/// 模拟盘请求额外携带 x-simulated-trading: 1。
/// </summary>
public static class OkxSigner
{
    public const string HeaderApiKey = "OK-ACCESS-KEY";
    public const string HeaderSign = "OK-ACCESS-SIGN";
    public const string HeaderTimestamp = "OK-ACCESS-TIMESTAMP";
    public const string HeaderPassphrase = "OK-ACCESS-PASSPHRASE";
    public const string HeaderSimulatedTrading = "x-simulated-trading";

    /// <summary>生成 UTC ISO8601 毫秒时间戳（与签名 prehash 使用同一字符串）。</summary>
    public static string CreateTimestamp() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>拼接待签名字符串：timestamp + method + requestPathWithQuery + body。</summary>
    public static string BuildPrehash(string timestamp, string method, string requestPathWithQuery, string body) =>
        timestamp + method.ToUpperInvariant() + requestPathWithQuery + body;

    /// <summary>HMAC-SHA256 签名并转 Base64。</summary>
    public static string Sign(string secretKey, string prehash)
    {
        if (string.IsNullOrEmpty(secretKey))
            throw new ArgumentException("欧易 Secret Key 不能为空", nameof(secretKey));

        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secretKey),
            Encoding.UTF8.GetBytes(prehash));
        return Convert.ToBase64String(hash);
    }

    /// <summary>为已创建好的请求写入欧易鉴权头（时间戳需与签名时一致）。</summary>
    public static void Apply(HttpRequestMessage request, OkxConfig config, string timestamp, string requestPathWithQuery, string? body)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(config);

        var prehash = BuildPrehash(timestamp, request.Method.Method, requestPathWithQuery, body ?? string.Empty);
        var sign = Sign(config.SecretKey, prehash);

        request.Headers.TryAddWithoutValidation(HeaderApiKey, config.ApiKey);
        request.Headers.TryAddWithoutValidation(HeaderSign, sign);
        request.Headers.TryAddWithoutValidation(HeaderTimestamp, timestamp);
        request.Headers.TryAddWithoutValidation(HeaderPassphrase, config.Passphrase);
        if (config.SimulatedTrading)
            request.Headers.TryAddWithoutValidation(HeaderSimulatedTrading, "1");
    }
}
