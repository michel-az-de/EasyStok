using System.Security.Claims;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public async Task<IActionResult> Unlink(string id, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;
        var batch = await db.Set<Batch>().FirstOrDefaultAsync(b => b.Id == id && b.EmpresaId == emp, ct);
        if (batch == null) return NotFound();
        batch.ErpLoteId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
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
