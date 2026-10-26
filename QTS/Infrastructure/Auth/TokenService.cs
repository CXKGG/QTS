using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QTS.Domain;
using QTS.Services;

namespace QTS.Infrastructure.Auth;

/// <summary>
/// 轻量 HMAC-SHA256 token：base64url(payload).base64url(signature)。
/// payload = userId \n base64(username) \n unixExpires \n nonce。无需引入 JWT 依赖。
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly AuthOptions _options;

    public TokenService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Secret) || _options.Secret.Length < 16)
            throw new InvalidOperationException("Auth:Secret 至少需要 16 个字符");
    }

    private byte[] SigningKey => SHA256.HashData(Encoding.UTF8.GetBytes(_options.Secret));

    public string CreateToken(UserEntity user, out DateTime expiresAt)
    {
        expiresAt = DateTime.UtcNow.AddHours(Math.Clamp(_options.TokenHours, 1, 24 * 30));
        var username = Convert.ToBase64String(Encoding.UTF8.GetBytes(user.Username));
        var payload = $"{user.Id}\n{username}\n{new DateTimeOffset(expiresAt).ToUnixTimeSeconds()}\n{Guid.NewGuid():N}";
        var body = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signature = Sign(body);
        return $"{body}.{signature}";
    }

    public bool TryValidateToken(string token, out long userId, out string username)
    {
        userId = 0;
        username = "";
        if (string.IsNullOrWhiteSpace(token)) return false;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return false;
        var body = token[..dot];
        var signature = token[(dot + 1)..];
        var expected = Sign(body);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(signature), Encoding.ASCII.GetBytes(expected)))
            return false;

        try
        {
            var payload = Encoding.UTF8.GetString(Base64UrlDecode(body));
            var parts = payload.Split('\n');
            if (parts.Length != 4) return false;
            if (!long.TryParse(parts[0], out userId)) return false;
            username = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
            if (!long.TryParse(parts[2], out var expiresUnix)) return false;
            if (DateTimeOffset.FromUnixTimeSeconds(expiresUnix) <= DateTimeOffset.UtcNow) return false;
            return userId > 0 && !string.IsNullOrWhiteSpace(username);
        }
        catch
        {
            userId = 0;
            username = "";
            return false;
        }
    }

    private string Sign(string body)
    {
        using var hmac = new HMACSHA256(SigningKey);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(body)));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
