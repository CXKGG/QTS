namespace QTS.Infrastructure.Auth;

public sealed class AuthOptions
{
    public string Secret { get; set; } = "qts-dev-secret-change-me-in-production-2026";
    public int TokenHours { get; set; } = 24;
    public string Issuer { get; set; } = "QTS";
}
