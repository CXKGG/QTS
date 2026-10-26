using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using QTS.Contracts;
using QTS.Services;

namespace QTS.Infrastructure.Auth;

public sealed class BearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Bearer";
    private readonly ITokenService _tokens;
    private readonly JsonSerializerOptions _json;

    public BearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITokenService tokens) : base(options, logger, encoder)
    {
        _tokens = tokens;
        _json = QtsJson.Options;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return Task.FromResult(AuthenticateResult.NoResult());
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.Fail("认证头格式无效"));

        var token = header["Bearer ".Length..].Trim();
        if (!_tokens.TryValidateToken(token, out var userId, out var username))
            return Task.FromResult(AuthenticateResult.Fail("登录已过期，请重新登录"));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username)
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json; charset=utf-8";
        var body = ApiResponse<object>.Fail("未登录或登录已过期，请重新登录", "UNAUTHORIZED");
        await Response.WriteAsync(JsonSerializer.Serialize(body, _json));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json; charset=utf-8";
        var body = ApiResponse<object>.Fail("没有权限访问该资源", "FORBIDDEN");
        await Response.WriteAsync(JsonSerializer.Serialize(body, _json));
    }
}
