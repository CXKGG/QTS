using QTS.Contracts;
using QTS.Domain;
using QTS.Infrastructure.Auth;
using QTS.Services;
using SqlSugar;
using System.Text.RegularExpressions;

namespace QTS.Services;

public sealed class AuthService : IAuthService
{
    private static readonly Regex UsernamePattern = new(@"^[\p{L}\p{N}_-]{3,32}$", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly ISqlSugarClient _db;
    private readonly PasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ILogger<AuthService> _logger;

    public AuthService(ISqlSugarClient db, PasswordHasher hasher, ITokenService tokens, ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new BusinessException("请输入用户名与密码", "INVALID_CREDENTIALS", 401);

        var user = await _db.Queryable<UserEntity>().FirstAsync(x => x.Username == username.Trim());
        if (user is null || !_hasher.Verify(password, user.PasswordHash))
        {
            _logger.LogWarning("登录失败，用户名 {Username}", username);
            throw new BusinessException("用户名或密码错误", "INVALID_CREDENTIALS", 401);
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _db.Updateable(user).UpdateColumns(x => new { x.LastLoginAt }).ExecuteCommandAsync();

        var token = _tokens.CreateToken(user, out var expiresAt);
        return new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = ToDto(user)
        };
    }

    public async Task<LoginResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new BusinessException("注册信息不能为空");

        var username = (request.Username ?? "").Trim();
        if (!UsernamePattern.IsMatch(username))
            throw new BusinessException("用户名需为 3-32 位字母、数字、下划线、短横线或中文，且不能包含空格");
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6 || request.Password.Length > 64)
            throw new BusinessException("密码长度需为 6-64 位");
        if (!string.Equals(request.Password, request.Password.Trim(), StringComparison.Ordinal))
            throw new BusinessException("密码首尾不能包含空格");

        var email = NormalizeEmail(request.Email);
        if (await _db.Queryable<UserEntity>().AnyAsync(x => x.Username == username))
            throw new BusinessException("该用户名已被注册", "USERNAME_TAKEN", 409);

        var displayName = (request.DisplayName ?? "").Trim();
        if (displayName.Length == 0) displayName = username;
        if (displayName.Length > 32) throw new BusinessException("昵称不能超过 32 个字符");

        var now = DateTime.UtcNow;
        var user = new UserEntity
        {
            Username = username,
            PasswordHash = _hasher.Hash(request.Password),
            DisplayName = displayName,
            Email = email,
            Role = "user",
            CreatedAt = now,
            LastLoginAt = now
        };
        user.Id = await _db.Insertable(user).ExecuteReturnBigIdentityAsync();

        var token = _tokens.CreateToken(user, out var expiresAt);
        _logger.LogInformation("新用户注册：{Username}", username);
        return new LoginResponse
        {
            Token = token,
            ExpiresAt = expiresAt,
            User = ToDto(user)
        };
    }

    public async Task<UserDto> UpdateProfileAsync(long userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await _db.Queryable<UserEntity>().FirstAsync(x => x.Id == userId);
        if (user is null) throw new BusinessException("用户不存在", "NOT_FOUND", 404);

        var displayName = (request.DisplayName ?? "").Trim();
        if (displayName.Length == 0) throw new BusinessException("昵称不能为空");
        if (displayName.Length > 32) throw new BusinessException("昵称不能超过 32 个字符");

        user.DisplayName = displayName;
        user.Email = NormalizeEmail(request.Email);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(user).UpdateColumns(x => new { x.DisplayName, x.Email, x.UpdatedAt }).ExecuteCommandAsync();
        return ToDto(user);
    }

    public async Task<UserDto?> GetUserAsync(long userId, CancellationToken ct = default)
    {
        var user = await _db.Queryable<UserEntity>().FirstAsync(x => x.Id == userId);
        return user is null ? null : ToDto(user);
    }

    public async Task<bool> ChangePasswordAsync(long userId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            throw new BusinessException("新密码至少需要 6 个字符");

        var user = await _db.Queryable<UserEntity>().FirstAsync(x => x.Id == userId);
        if (user is null) throw new BusinessException("用户不存在", "NOT_FOUND", 404);
        if (!_hasher.Verify(oldPassword, user.PasswordHash))
            throw new BusinessException("原密码不正确");

        user.PasswordHash = _hasher.Hash(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.Updateable(user).UpdateColumns(x => new { x.PasswordHash, x.UpdatedAt }).ExecuteCommandAsync();
        return true;
    }

    private static string? NormalizeEmail(string? email)
    {
        var value = (email ?? "").Trim();
        if (value.Length == 0) return null;
        if (value.Length > 128 || !EmailPattern.IsMatch(value))
            throw new BusinessException("请输入有效的邮箱地址");
        return value;
    }

    private static UserDto ToDto(UserEntity user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        Email = user.Email,
        Role = string.IsNullOrWhiteSpace(user.Role) ? "user" : user.Role,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt
    };
}
