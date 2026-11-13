using System.Security.Claims;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Onda P5.A — Revisão e linkagem de lotes mobile↔ERP.
/// Auditoria 2026-04-30: tenant guard via <see cref="MobileManagementControllerBase"/>.
/// </summary>
[ApiController]
[Route("api/mobile/batches")]
[Authorize]
public class MobileBatchesController(
    EasyStockDbContext db,
    ILoteRepository loteRepo,
    CriarLoteUseCase criarLoteUseCase,
    ICurrentUserAccessor currentUser,
    IFileStorage fileStorage,
    ILogger<MobileBatchesController> log) : MobileManagementControllerBase(currentUser)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? empresaId,
        [FromQuery] bool? pendingOnly,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var q = db.Set<Batch>().AsNoTracking().Where(b => b.EmpresaId == emp);
        if (pendingOnly == true) q = q.Where(b => b.ErpLoteId == null);

        var items = await q.OrderByDescending(b => b.CreatedAt).Take(500).ToListAsync(ct);

        return Ok(items.Select(b => new MobileBatchSummary(
            b.Id, b.Code, b.Lote, b.CreatedAt,
            b.EmpresaId, b.LojaId, b.ErpLoteId,
            b.LastDeviceId, b.LastOperatorName
        )).ToArray());
    }

    [HttpPost("{id}/link")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Link(string id, [FromBody] LinkLoteRequest? req, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var batch = await db.Set<Batch>().Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id && b.EmpresaId == emp, ct);
        if (batch == null) return NotFound();
        if (batch.EmpresaId == null || batch.EmpresaId == Guid.Empty)
            return BadRequest(new { error = "mobile_batch sem empresa associada" });

        Guid erpLoteId;

        if (req?.ErpLoteId is { } existing && existing != Guid.Empty)
        {
            var lote = await loteRepo.GetByIdAsync(batch.EmpresaId.Value, existing);
            if (lote == null)
                return BadRequest(new { error = "Lote ERP não encontrado nesta empresa" });
            erpLoteId = lote.Id;
        }
        else
        {
            // Auditoria 2026-04-30 (idempotência): se já existe Lote ERP
            // com este MobileBatchId, retorna sem duplicar.
            var jaPromovido = await loteRepo.FindByMobileBatchIdAsync(batch.EmpresaId.Value, batch.Id);
            if (jaPromovido != null)
            {
                batch.ErpLoteId = jaPromovido.Id;
                await db.SaveChangesAsync(ct);
                log.LogInformation("Mobile batch {MobileId} já promovido a {ErpId} (idempotente).", id, jaPromovido.Id);
                return Ok(new { erpLoteId = jaPromovido.Id });
            }

            var itens = batch.Items.Select(i => new CriarLoteItemInput(
                Nome: i.Name,
                Quantidade: i.Qty,
                ProdutoId: null,
                Emoji: i.Emoji,
                Unidade: i.Unit,
                PesoG: i.WeightG,
                ValidadeDias: i.ValidityDays,
                FotoUrl: i.Photo
            )).ToList();

            var result = await criarLoteUseCase.ExecuteAsync(new CriarLoteCommand(
                EmpresaId: batch.EmpresaId.Value,
                LojaId: batch.LojaId,
                DataProducao: batch.CreatedAt,
                CodigoCustom: batch.Code,
                OperadorUserId: ResolveUserId(),
                OperadorNome: batch.LastOperatorName,
                Observacoes: null,
                FotoUrl: batch.BatchPhoto,
                Origem: "mobile",
                MobileBatchId: batch.Id,
                Itens: itens
            ));
            erpLoteId = result.Id;
        }

        batch.ErpLoteId = erpLoteId;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Mobile batch {MobileId} linkado a Lote ERP {ErpId}.", id, erpLoteId);
        return Ok(new { erpLoteId });
    }

    [HttpPost("{id}/unlink")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Unlink(string id, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;
        var batch = await db.Set<Batch>().FirstOrDefaultAsync(b => b.Id == id && b.EmpresaId == emp, ct);
        if (batch == null) return NotFound();
        batch.ErpLoteId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// F10-C-7 — Upload de foto de batch via device mobile.
    /// Recebe multipart com campo "photo" (Blob), "hash" (FNV-1a) e "field" (batchPhoto | item:N).
    /// Idempotente: se foto com mesmo hash já existe, retorna 409.
    /// Auth via MobileApiKey (device mobile), não JWT.
    /// </summary>
    [HttpPost("{id}/photos")]
    [AllowAnonymous]
    [MobileApiKey]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB max
    public async Task<IActionResult> UploadPhoto(
        string id,
        [FromForm] string hash,
        [FromForm] string field,
        IFormFile photo,
        CancellationToken ct)
    {
        // Resolve empresa via device autenticado (MobileApiKey já setou HttpContext.Items)
        var device = HttpContext.Items["mobile-device"] as MobileDevice;
        if (device == null) return Unauthorized();
        var empresaId = device.EmpresaId;
        if (!IdSeguro(id)) return NotFound(new { error = "Batch not found" });

        // Busca o batch
        var batch = await db.Set<Batch>().Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id && b.EmpresaId == empresaId, ct);
        if (batch == null) return NotFound(new { error = "Batch not found" });

        if (photo == null || photo.Length == 0)
            return BadRequest(new { error = "photo is required" });

        // Upload para file storage — sanitize hash to prevent path traversal
        var bucketPath = PastaFotos(empresaId, id);
        var ext = photo.ContentType.Contains("png") ? ".png" : ".jpg";
        var safeHash = SanitizeFileName(hash);
        var fileName = $"{safeHash}{ext}";

        byte[] content;
        using (var ms = new MemoryStream())
        {
            await photo.CopyToAsync(ms, ct);
            content = ms.ToArray();
        }

        var result = await fileStorage.UploadAsync(new FileUploadRequest(
            BucketPath: bucketPath,
            FileName: fileName,
            ContentType: photo.ContentType,
            Content: content,
            IsPublic: false
        ), ct);

        // #2459: a URL é a da rota autenticada; result.Url apontaria para o /files público.
        var url = $"/api/mobile/batches/{Uri.EscapeDataString(id)}/photos/{result.StorageKey[(result.StorageKey.LastIndexOf('/') + 1)..]}";

        // Atualiza o campo correto no batch
        if (field == "batchPhoto")
        {
            batch.BatchPhoto = url;
        }
        else if (field != null && field.StartsWith("item:"))
        {
            if (int.TryParse(field.AsSpan(5), out var idx) && idx >= 0 && idx < batch.Items.Count)
            {
                // Items ordenados por Id — match pelo índice
                var sortedItems = batch.Items.OrderBy(i => i.Id).ToList();
                if (idx < sortedItems.Count)
                    sortedItems[idx].Photo = url;
            }
        }

        await db.SaveChangesAsync(ct);

        log.LogInformation("Photo uploaded for batch {BatchId} field {Field} hash {Hash} → {Url}",
            SanitizarParaLog(id), SanitizarParaLog(field), SanitizarParaLog(safeHash), SanitizarParaLog(url));

        return Ok(new { url, hash });
    }

    /// <summary>
    /// #2459: serve a foto gravada por <see cref="UploadPhoto"/>. O <c>/files</c> público bloqueia
    /// <c>mobile-photos</c>; aqui só o aparelho da empresa dona do lote lê.
    /// </summary>
    [HttpGet("{id}/photos/{arquivo}")]
    [AllowAnonymous]
    [MobileApiKey]
    public async Task<IActionResult> ObterFoto(string id, string arquivo, CancellationToken ct)
    {
        if (HttpContext.Items[MobileAuth.HttpContextItemDevice] is not MobileDevice device) return Unauthorized();
        var ext = Path.GetExtension(arquivo);
        var nome = Path.GetFileNameWithoutExtension(arquivo);
        var contentType = ext switch { ".jpg" => "image/jpeg", ".png" => "image/png", _ => null };
        if (!IdSeguro(id) || contentType is null || nome.Length == 0 || arquivo != nome + ext || SanitizeFileName(nome) != nome)
            return NotFound();

        var empresaId = device.EmpresaId;
        if (!await db.Set<Batch>().AnyAsync(b => b.Id == id && b.EmpresaId == empresaId, ct)) return NotFound();

        var chave = $"{PastaFotos(empresaId, id)}/{nome}{ext}";
        if (!await fileStorage.ExistsAsync(chave, ct)) return NotFound();

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(await fileStorage.DownloadStreamAsync(chave, ct), contentType);
    }

    private static string PastaFotos(Guid? empresaId, string batchId) => $"mobile-photos/{empresaId}/{batchId}";

    // id, field, hash e url vêm do aparelho: sem quebra de linha não forjam entrada de log.
    private static string SanitizarParaLog(string? valor) =>
        (valor ?? "").Replace("\r", "").Replace("\n", "");

    // O id do lote vem do aparelho e vira pasta: separador ou ".." tiraria a chave da pasta da empresa.
    private static bool IdSeguro(string id) => id.IndexOfAny(['/', '\\']) < 0 && !id.Contains("..");

    /// <summary>
    /// Sanitiza hash para uso seguro em filename — remove path traversal e caracteres especiais.
    /// </summary>
    private static string SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Guid.NewGuid().ToString();
        // Remove path separators and dangerous chars, keep alphanum + dash + underscore
        var sanitized = new string(raw.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return string.IsNullOrEmpty(sanitized) ? Guid.NewGuid().ToString() : sanitized;
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var g) ? g : null;
    }
}

public record MobileBatchSummary(
    string Id,
    string Code,
    string? Lote,
    DateTime CreatedAt,
    Guid? EmpresaId,
    Guid? LojaId,
    Guid? ErpLoteId,
    string? LastDeviceId,
    string? LastOperatorName
);

public record LinkLoteRequest(Guid? ErpLoteId);
