using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Ports.Output.Reporting;

/// <summary>
/// Link de download do artefato de um relatório (#2668). No storage local o link é um token assinado e com validade
/// que a própria API serve; no S3 é a URL pré-assinada do provedor.
/// </summary>
public interface IRelatorioDownloadLink
{
    Task<Uri> CriarAsync(ReportRun run, string nomeArquivo, TimeSpan validade, CancellationToken ct);

    /// <summary>O que o token autoriza, ou null se ele foi adulterado, venceu ou não é deste propósito.</summary>
    RelatorioDownloadAutorizado? Validar(string? token);
}

public sealed record RelatorioDownloadAutorizado(Guid EmpresaId, Guid RunId, string StorageKey);
