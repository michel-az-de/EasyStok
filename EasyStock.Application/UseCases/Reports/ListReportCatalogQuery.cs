using EasyStock.Application.Ports.Output;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Dtos;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.UseCases.Reports;

public sealed record ListReportCatalogQuery(
    ReportCategoria? CategoriaFiltro = null) : ICommand;

public sealed class ListReportCatalogUseCase
    : IUseCase<ListReportCatalogQuery, IReadOnlyList<ReportCatalogItemDto>>
{
    private readonly ReportRegistry _registry;
    private readonly ICurrentUserAccessor _user;

    public ListReportCatalogUseCase(
        ReportRegistry registry,
        ICurrentUserAccessor user)
    {
        _registry = registry;
        _user = user;
    }

    public Task<IReadOnlyList<ReportCatalogItemDto>> ExecuteAsync(ListReportCatalogQuery query)
    {
        var isSuperAdmin = _user.Nivel == EasyStock.Domain.Enums.NivelAcesso.SuperAdmin;

        var items = _registry.All()
            .Where(d =>
                // Tenant vê apenas relatórios de contexto Tenant
                // SuperAdmin vê tudo
                (isSuperAdmin || d.Contexto == ReportContexto.Tenant) &&
                // Filtro de categoria opcional
                (query.CategoriaFiltro is null || d.Categoria == query.CategoriaFiltro))
            .Select(d => new ReportCatalogItemDto(
                Key: d.Key,
                Label: d.Label,
                Descricao: d.Descricao,
                Categoria: d.Categoria,
                Contexto: d.Contexto,
                IconKey: d.IconKey,
                FormatosSuportados: d.FormatosSuportados,
                PermissaoRequerida: d.PermissaoRequerida,
                SemanticVersion: d.SemanticVersion,
                SchemaUrl: $"/api/v1/reports/{d.Key}/schema"))
            .ToList();

        return Task.FromResult<IReadOnlyList<ReportCatalogItemDto>>(items);
    }
}
