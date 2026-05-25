namespace EasyStock.Domain.Entities;

/// <summary>
/// Registro persistente de erros do sistema originados de múltiplas fontes.
/// Usado para rastreabilidade durante demos/testes — purgável via admin.
/// </summary>
public class SystemErrorLog
{
    public Guid Id { get; set; }

    /// <summary>api_backend | seed | admin_frontend | web_frontend | background</summary>
    public string Source { get; set; } = "";

    /// <summary>error | fatal | warning</summary>
    public string Level { get; set; } = "error";

    /// <summary>api_exception | seed_failure | component_load | js_error | job_failure</summary>
    public string? Category { get; set; }

    public string Message { get; set; } = "";

    /// <summary>JSON com contexto: stackTrace, requestMethod, requestPath, statusCode, exceptionType, etc.</summary>
    public string? Details { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>URL da requisição HTTP ou pathname da página frontend.</summary>
    public string? Url { get; set; }

    public string? AdminEmail { get; set; }

    public Guid? TenantId { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
