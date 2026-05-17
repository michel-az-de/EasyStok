using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Faturas.GerarPdfFatura;

/// <summary>Command de geracao (ou recuperacao) de PDF de fatura.</summary>
/// <param name="EmpresaId">Empresa do cliente quando <c>Admin == false</c>; ignorado em modo admin.</param>
/// <param name="FaturaId">Identificador da fatura.</param>
/// <param name="Admin">true = admin (sem filtro EmpresaId); false = cliente (filtra por currentUser).</param>
/// <param name="ForcarRegenerar">true = ignora cache e regenera. Padrao false (usa cache se existir).</param>
public sealed record GerarPdfFaturaCommand(
    Guid? EmpresaId,
    Guid FaturaId,
    bool Admin = false,
    bool ForcarRegenerar = false
);

public sealed record GerarPdfFaturaResult(
    byte[] Bytes,
    string ContentType,
    string FileName,
    string StorageKey,
    bool VeioDoCache,
    /// <summary>EmpresaId da fatura — controllers admin operacional usam para checagem cross-tenant.</summary>
    Guid EmpresaId
);

/// <summary>
/// Gera (ou recupera do cache) o PDF de uma <see cref="Fatura"/>.
///
/// <para>
/// Estrategia em camadas:
/// </para>
/// <list type="number">
///   <item>Se <see cref="Fatura.PdfStorageKey"/> existe e <c>ForcarRegenerar</c> e false,
///   baixa do <see cref="IFileStorage"/> e retorna (cache hit).</item>
///   <item>Caso contrario, renderiza via <see cref="IFaturaPdfRenderer"/>, faz upload
///   para <c>faturas/{empresaId:N}/{faturaId:N}.pdf</c>, atualiza
///   <see cref="Fatura.PdfStorageKey"/>, persiste, retorna o byte[].</item>
/// </list>
///
/// <para>
/// Faturas em estado <see cref="StatusFatura.Rascunho"/> ainda podem ser geradas
/// (preview admin) mas marcam o PDF com indicacao no template (futuro).
/// </para>
/// </summary>
public class GerarPdfFaturaUseCase(
    IFaturaRepository repo,
    IFaturaPdfRenderer renderer,
    IFileStorage fileStorage,
    IUnitOfWork uow,
    ILogger<GerarPdfFaturaUseCase> logger)
{
    public async Task<GerarPdfFaturaResult?> ExecuteAsync(
        GerarPdfFaturaCommand cmd,
        CancellationToken ct = default)
    {
        UseCaseGuards.EnsureNotEmpty(cmd.FaturaId, nameof(cmd.FaturaId));

        var fatura = cmd.Admin
            ? await repo.GetByIdAdminAsync(cmd.FaturaId, ct)
            : (cmd.EmpresaId.HasValue
                ? await repo.GetByIdAsync(cmd.EmpresaId.Value, cmd.FaturaId, ct)
                : null);

        if (fatura is null) return null;

        var fileName = $"fatura-{fatura.Numero}.pdf";

        // Cache hit
        if (!cmd.ForcarRegenerar && !string.IsNullOrWhiteSpace(fatura.PdfStorageKey))
        {
            try
            {
                var cached = await fileStorage.DownloadAsync(fatura.PdfStorageKey, ct);
                if (cached is { Length: > 0 })
                {
                    logger.LogDebug("PDF cache hit. FaturaId={FaturaId} StorageKey={Key}",
                        fatura.Id, fatura.PdfStorageKey);
                    return new GerarPdfFaturaResult(cached, "application/pdf", fileName, fatura.PdfStorageKey, true, fatura.EmpresaId);
                }
            }
            catch (Exception ex)
            {
                // Cache miss por arquivo removido/expirado — regenera silenciosamente.
                logger.LogWarning(ex,
                    "Falha ao baixar PDF do cache (StorageKey={Key}). Regenerando.", fatura.PdfStorageKey);
            }
        }

        // Render + upload
        var bytes = await renderer.RenderAsync(fatura, ct);

        var bucketPath = $"faturas/{fatura.EmpresaId:N}";
        var stored = await fileStorage.UploadAsync(
            new FileUploadRequest(
                BucketPath: bucketPath,
                FileName: $"{fatura.Id:N}.pdf",
                ContentType: "application/pdf",
                Content: bytes,
                IsPublic: false),
            ct);

        fatura.PdfStorageKey = stored.StorageKey;
        await repo.UpdateAsync(fatura, ct);
        await uow.CommitAsync();

        logger.LogInformation(
            "PDF de fatura gerado. FaturaId={FaturaId} Numero={Numero} Tamanho={Size}B",
            fatura.Id, fatura.Numero, bytes.Length);

        return new GerarPdfFaturaResult(bytes, "application/pdf", fileName, stored.StorageKey, false, fatura.EmpresaId);
    }
}
