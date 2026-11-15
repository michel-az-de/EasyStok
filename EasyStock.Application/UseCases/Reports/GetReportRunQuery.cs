using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Dtos;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.UseCases.Reports;

public sealed record GetReportRunQuery(Guid RunId) : ICommand;

public sealed class GetReportRunUseCase
    : IUseCase<GetReportRunQuery, ReportRunDto?>
{
    private static readonly TimeSpan DownloadUrlTtl = TimeSpan.FromMinutes(15);

    private readonly IReportRunRepository _repo;
    private readonly ReportRegistry       _registry;
    private readonly IRelatorioDownloadLink _link;
    private readonly ICurrentUserAccessor _user;

    public GetReportRunUseCase(
        IReportRunRepository repo,
        ReportRegistry       registry,
        IRelatorioDownloadLink link,
        ICurrentUserAccessor user)
    {
        _repo     = repo;
        _registry = registry;
        _link     = link;
        _user     = user;
    }

    public async Task<ReportRunDto?> ExecuteAsync(GetReportRunQuery query)
    {
        var ct  = CancellationToken.None;
        var run = await _repo.GetByIdAsync(query.RunId, ct);
        if (run is null) return null;

        // Tenant isolation: verifica que a run pertence ao usuário/empresa
        if (!IsOwner(run)) return null;

        var definition = _registry.Find(run.ReportKey);

        string?         downloadUrl       = null;
        DateTimeOffset? downloadExpiresAt = null;

        if (run.Status == ReportStatus.Succeeded && run.ArtifactStorageKey is not null)
        {
            var expira    = DateTimeOffset.UtcNow.Add(DownloadUrlTtl);
            downloadUrl       = (await _link.CriarAsync(run, ReportArtefato.NomeArquivo(run), DownloadUrlTtl, ct)).ToString();
            downloadExpiresAt = expira;
        }

        return MapToDto(run, definition, downloadUrl, downloadExpiresAt);
    }

    private bool IsOwner(ReportRun run)
    {
        if (_user.Nivel == EasyStock.Domain.Enums.NivelAcesso.SuperAdmin) return true;
        return run.Contexto == ReportContexto.Tenant &&
               run.EmpresaId == _user.EmpresaId &&
               run.UsuarioSolicitanteId == _user.UsuarioId;
    }

    private static ReportRunDto MapToDto(
        ReportRun         run,
        IReportDefinition? def,
        string?           downloadUrl,
        DateTimeOffset?   downloadExpiresAt) =>
        new(
            Id:               run.Id,
            ReportKey:        run.ReportKey,
            ReportLabel:      def?.Label ?? run.ReportKey,
            Categoria:        run.Categoria,
            Contexto:         run.Contexto,
            Format:           run.Format,
            Status:           run.Status,
            StatusLabel:      run.Status.ToString(),
            ParamsJson:       run.ParamsJson,
            Tentativas:       run.Tentativas,
            MaxTentativas:    run.MaxTentativas,
            RowCount:         run.RowCount,
            ArtifactSizeBytes: run.ArtifactSizeBytes,
            ErrorMessage:     run.ErrorMessage,
            WarningsJson:     run.WarningsJson,
            SemanticVersion:  run.SemanticVersion,
            EnqueuedAt:       run.EnqueuedAt,
            StartedAt:        run.StartedAt,
            FinishedAt:       run.FinishedAt,
            ExpiresAt:        run.ExpiresAt,
            DownloadUrl:      downloadUrl,
            DownloadExpiresAt: downloadExpiresAt,
            CanDownload:      run.Status == ReportStatus.Succeeded && run.ArtifactStorageKey is not null);
}
