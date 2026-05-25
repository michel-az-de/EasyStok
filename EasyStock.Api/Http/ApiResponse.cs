namespace EasyStock.Api.Http;

/// <summary>Success envelope: { data, meta }</summary>
public sealed record ApiResponse<T>(T Data, object Meta);

/// <summary>Pagination metadata carried inside meta.</summary>
public sealed record PagedMeta(int Total, int Pages, int Page, int Limit);

/// <summary>Error envelope: { error }</summary>
public sealed record ApiErrorResponse(ApiError Error);

/// <summary>Structured error detail.</summary>
public sealed record ApiError(
    string Code,
    string Message,
    string? Detail,
    string? CorrelationId)
{
    public string? Recurso { get; init; }

    /// <summary>
    /// Dados estruturados auxiliares para erros estruturados (ex: lista de fornecedores
    /// com erro em SUPPLIER_INACTIVE, ciclo detectado em CYCLE_DETECTED). Opcional.
    /// </summary>
    public object? Details { get; init; }
}
