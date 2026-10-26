using System.Text.Json;
using QTS.Contracts;

namespace QTS.Infrastructure.Middleware;

/// <summary>把 BusinessException 和未处理异常统一转换为 ApiResponse 信封。</summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BusinessException ex)
        {
            _logger.LogWarning("业务异常 {Path}: {Message}", context.Request.Path, ex.Message);
            await WriteAsync(context, ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.Code));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端主动取消，不写响应。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常 {Method} {Path}", context.Request.Method, context.Request.Path);
            await WriteAsync(context, StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail("服务器内部错误，请稍后重试", "INTERNAL_ERROR"));
        }
    }

    private async Task WriteAsync(HttpContext context, int statusCode, object body)
    {
        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(JsonSerializer.Serialize(body, QtsJson.Options));
    }
}
