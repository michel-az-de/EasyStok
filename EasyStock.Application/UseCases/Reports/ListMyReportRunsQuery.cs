using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Dtos;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.UseCases.Reports;

public sealed record ListMyReportRunsQuery(
    ReportListFilter Filter,
    int              Skip = 0,
    int              Take = 25) : ICommand;

public sealed class ListMyReportRunsUseCase
    : IUseCase<ListMyReportRunsQuery, IReadOnlyList<ReportRunDto>>
{
    private readonly IReportRunRepository _repo;
    private readonly ReportRegistry       _registry;
    private readonly ICurrentUserAccessor _user;

    public ListMyReportRunsUseCase(
        IReportRunRepository repo,
        ReportRegistry       registry,
        ICurrentUserAccessor user)
    {
        _repo     = repo;
        _registry = registry;
        _user     = user;
    }

    public async Task<IReadOnlyList<ReportRunDto>> ExecuteAsync(ListMyReportRunsQuery query)
    {
        var ct   = CancellationToken.None;
        var take = Math.Min(query.Take, 100); // hard cap

        var empresaId = _user.Nivel == EasyStock.Domain.Enums.NivelAcesso.SuperAdmin
            ? (Guid?)null
            : _user.EmpresaId;

        var runs = await _repo.ListMineAsync(
            empresaId, _user.UsuarioId, query.Filter, query.Skip, take, ct);

        return runs.Select(r =>
        {
            var def = _registry.Find(r.ReportKey);
            return new ReportRunDto(
                Id:               r.Id,
                ReportKey:        r.ReportKey,
                ReportLabel:      def?.Label ?? r.ReportKey,
                Categoria:        r.Categoria,
                Contexto:         r.Contexto,
                Format:           r.Format,
                Status:           r.Status,
                StatusLabel:      r.Status.ToString(),
                ParamsJson:       r.ParamsJson,
                Tentativas:       r.Tentativas,
                MaxTentativas:    r.MaxTentativas,
                RowCount:         r.RowCount,
                ArtifactSizeBytes: r.ArtifactSizeBytes,
                ErrorMessage:     r.ErrorMessage,
                WarningsJson:     r.WarningsJson,
                SemanticVersion:  r.SemanticVersion,
                EnqueuedAt:       r.EnqueuedAt,
                StartedAt:        r.StartedAt,
                FinishedAt:       r.FinishedAt,
                ExpiresAt:        r.ExpiresAt,
                CanDownload:      r.Status == ReportStatus.Succeeded && r.ArtifactStorageKey is not null);
        }).ToList();
    }
}
