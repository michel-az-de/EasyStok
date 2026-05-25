using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Dtos;

public sealed record ReportCatalogItemDto(
    string Key,
    string Label,
    string Descricao,
    ReportCategoria Categoria,
    ReportContexto Contexto,
    string IconKey,
    IReadOnlyList<ReportFormat> FormatosSuportados,
    string PermissaoRequerida,
    string SemanticVersion,
    string SchemaUrl);
