using System.Security.Cryptography;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace EasyStock.Api.Services.Relatorios;

/// <summary>
/// #2668: o <see cref="LocalFileStorage"/> devolvia <c>/api/files/local-signed/...</c>, rota que não existe e cujo
/// "token" era só base64 da chave, sem assinatura. Com storage local o link agora é um token do Data Protection com
/// validade (empresa, run e chave dentro), servido por <c>ReportArtefatosController</c>. Com S3 segue a URL
/// pré-assinada do provedor.
/// </summary>
public sealed class RelatorioDownloadLink(IDataProtectionProvider dataProtection, IFileStorage storage) : IRelatorioDownloadLink
{
    public const string Proposito = "EasyStok.Relatorios.Download.v1";
    public const string Rota = EasyStock.Api.Observability.Logging.CaminhoDoLog.PrefixoDoArtefatoDeRelatorio;

    private readonly ITimeLimitedDataProtector _protetor =
        dataProtection.CreateProtector(Proposito).ToTimeLimitedDataProtector();

    public async Task<Uri> CriarAsync(ReportRun run, string nomeArquivo, TimeSpan validade, CancellationToken ct)
    {
        var chave = run.ArtifactStorageKey
            ?? throw new InvalidOperationException("Run sem artefato não tem link de download.");

        if (storage is not LocalFileStorage)
            return await storage.CreatePreSignedDownloadUrlAsync(chave, validade, nomeArquivo, ct);

        var conteudo = $"{run.EmpresaId ?? Guid.Empty:N}|{run.Id:N}|{chave}";
        var token = _protetor.Protect(conteudo, DateTimeOffset.UtcNow.Add(validade));
        return new Uri($"{Rota}/{token}", UriKind.Relative);
    }

    public RelatorioDownloadAutorizado? Validar(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var partes = _protetor.Unprotect(token).Split('|', 3);
            if (partes.Length != 3
                || !Guid.TryParseExact(partes[0], "N", out var empresa)
                || !Guid.TryParseExact(partes[1], "N", out var run)
                || string.IsNullOrEmpty(partes[2]))
                return null;
            return new RelatorioDownloadAutorizado(empresa, run, partes[2]);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Adulterado, vencido, de outro propósito ou base64 inválido: tudo vira o mesmo "não".
            return null;
        }
    }
}
