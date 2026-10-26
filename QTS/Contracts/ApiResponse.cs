namespace QTS.Contracts;

/// <summary>统一响应信封：前端只读取 success/data/message。</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public string? Code { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, string? code = null) =>
        new() { Success = false, Data = default, Message = message, Code = code };
}

public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public long Total { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
