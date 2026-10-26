using Microsoft.AspNetCore.DataProtection;
using QTS.Services;

namespace QTS.Infrastructure.Auth;

/// <summary>使用 ASP.NET Core DataProtection 加密数据库中的 OKX 密钥。</summary>
public sealed class SecretProtector : ISecretProtector
{
    private const string Prefix = "qts:v1:";
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("QTS.OkxCredentials.v1");
    }

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return "";
        return Prefix + _protector.Protect(plainText);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return "";
        if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal)) return protectedText;
        try
        {
            return _protector.Unprotect(protectedText[Prefix.Length..]);
        }
        catch
        {
            // 密钥环丢失时不让整个系统崩溃，返回空值并在设置页重新录入。
            return "";
        }
    }
}
