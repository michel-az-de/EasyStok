using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.UseCases.Reports;

public sealed record ArtefatoRelatorio(Stream Conteudo, string ContentType, string NomeArquivo);

/// <summary>
/// Serve o artefato pelo link assinado (#2668). Anônimo: quem autoriza é o token, que carrega empresa, run e chave.
/// A empresa do token vira o tenant da requisição (sem bypass de RLS) e a run tem de bater com tudo o que o token
/// diz. Qualquer divergência devolve null, e o controller responde o mesmo 404.
/// </summary>
public sealed class BaixarArtefatoRelatorioUseCase(
    IRelatorioDownloadLink link,
    ITenantContextAccessor tenant,
    IReportRunRepository repo,
    IFileStorage storage)
{
    public async Task<ArtefatoRelatorio?> ExecuteAsync(string? token, CancellationToken ct)
    {
        var autorizado = link.Validar(token);
        if (autorizado is null) return null;

        tenant.SetCurrentTenant(autorizado.EmpresaId);

        var run = await repo.GetByIdAsync(autorizado.RunId, ct);
        if (run is null
            || run.Contexto != ReportContexto.Tenant
            || run.EmpresaId != autorizado.EmpresaId
            || run.Status != ReportStatus.Succeeded
            || run.ArtifactStorageKey != autorizado.StorageKey
            || run.ExpiresAt <= DateTimeOffset.UtcNow)
            return null;

        try
        {
            var conteudo = await storage.DownloadStreamAsync(run.ArtifactStorageKey, ct);
            return new ArtefatoRelatorio(conteudo, ReportArtefato.ContentType(run.Format), ReportArtefato.NomeArquivo(run));
        }
        catch (Exception e) when (e is ArgumentException or FileNotFoundException or DirectoryNotFoundException)
        {
            // Chave fora da raiz do storage ou artefato já podado pela retenção.
            return null;
        }
    }
}
