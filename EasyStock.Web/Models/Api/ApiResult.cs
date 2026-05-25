namespace EasyStock.Web.Models.Api;

public record ApiResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int HttpStatus { get; init; }
    // Correlation ID propagado do header X-Correlation-Id da resposta (quando presente).
    // Usado em toasts para o suporte rastrear o request específico.
    public string? CorrelationId { get; init; }

    public static ApiResult<T> Ok(T data) =>
        new() { Success = true, Data = data, HttpStatus = 200 };

    public static ApiResult<T> Fail(string code, string msg, int status = 0, string? correlationId = null) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = msg, HttpStatus = status, CorrelationId = correlationId };
}
