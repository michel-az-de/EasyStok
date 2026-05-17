using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting;

/// <summary>
/// Filtros para listagem de execuções de relatório (ListMyReportRunsQuery).
/// </summary>
public sealed record ReportListFilter(
    ReportCategoria? Categoria    = null,
    ReportStatus?    Status       = null,
    DateTimeOffset?  De           = null,
    DateTimeOffset?  Ate          = null);
