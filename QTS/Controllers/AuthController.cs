using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QTS.Contracts;
using QTS.Infrastructure;
using QTS.Services;

namespace QTS.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ApiResponse<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            throw new BusinessException("请输入用户名和密码");

        var result = await _auth.LoginAsync(request.Username.Trim(), request.Password, ct);
        if (result is null)
            throw new BusinessException("用户名或密码错误", "INVALID_CREDENTIALS", StatusCodes.Status401Unauthorized);

        return ApiResponse<LoginResponse>.Ok(result);
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ApiResponse<LoginResponse>> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(request, ct);
        return ApiResponse<LoginResponse>.Ok(result, "注册成功");
    }

    [HttpGet("me")]
    public async Task<ApiResponse<UserDto>> Me(CancellationToken ct)
    {
        var user = await _auth.GetUserAsync(User.CurrentUserId(), ct);
        if (user is null)
            throw new BusinessException("用户不存在", "USER_NOT_FOUND", StatusCodes.Status404NotFound);
        return ApiResponse<UserDto>.Ok(user);
    }

    [HttpPut("profile")]
    public async Task<ApiResponse<UserDto>> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await _auth.UpdateProfileAsync(User.CurrentUserId(), request, ct);
        return ApiResponse<UserDto>.Ok(user, "个人资料已更新");
    }

    [HttpPost("change-password")]
    public async Task<ApiResponse<bool>> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OldPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            throw new BusinessException("原密码和新密码不能为空");
        if (request.NewPassword.Length < 6)
            throw new BusinessException("新密码长度不能少于 6 位");

        var ok = await _auth.ChangePasswordAsync(User.CurrentUserId(), request.OldPassword, request.NewPassword, ct);
        if (!ok)
            throw new BusinessException("原密码错误", "INVALID_PASSWORD");
        return ApiResponse<bool>.Ok(true, "密码已更新");
    }
}
