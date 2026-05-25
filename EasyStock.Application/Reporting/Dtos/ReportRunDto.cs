using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Dtos;

public sealed record ReportRunDto(
    Guid Id,
    string ReportKey,
    string ReportLabel,
    ReportCategoria Categoria,
    ReportContexto Contexto,
    ReportFormat Format,
    ReportStatus Status,
    string StatusLabel,
    string ParamsJson,
    int Tentativas,
    int MaxTentativas,
    long? RowCount,
    long? ArtifactSizeBytes,
    string? ErrorMessage,
    string? WarningsJson,
    string? SemanticVersion,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset? ExpiresAt,
    // downloadUrl é omitido na listagem; presente apenas no detalhe (GetReportRunQuery)
    string? DownloadUrl = null,
    DateTimeOffset? DownloadExpiresAt = null,
    bool CanDownload = false);
