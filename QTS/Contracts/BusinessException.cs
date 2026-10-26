namespace QTS.Contracts;

/// <summary>可安全展示给前端的业务异常。</summary>
public sealed class BusinessException : Exception
{
    public string? Code { get; }
    public int StatusCode { get; }

    public BusinessException(string message, string? code = null, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
