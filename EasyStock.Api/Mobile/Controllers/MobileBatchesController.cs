using System.Security.Claims;
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
///
/// Espelha MobileClientsController/MobileOrdersController/MobileCashController:
/// gestor revisa batches criados no app, linka a Lote ERP existente OU
/// **promove** criando um Lote ERP novo a partir do mobile_batch (com items).
/// </summary>
[ApiController]
[Route("api/mobile/batches")]
public class MobileBatchesController(
    EasyStockDbContext db,
    ILoteRepository loteRepo,
    CriarLoteUseCase criarLoteUseCase,
    ILogger<MobileBatchesController> log) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<MobileBatchSummary[]>> List(
        [FromQuery] Guid empresaId,
        [FromQuery] bool? pendingOnly,
        CancellationToken ct)
    {
        if (empresaId == Guid.Empty) return BadRequest(new { error = "empresaId obrigatório" });

        var q = db.Set<Batch>().AsNoTracking().Where(b => b.EmpresaId == empresaId);
        if (pendingOnly == true) q = q.Where(b => b.ErpLoteId == null);

        var items = await q.OrderByDescending(b => b.CreatedAt).Take(500).ToListAsync(ct);

        return Ok(items.Select(b => new MobileBatchSummary(
            b.Id, b.Code, b.Lote, b.CreatedAt,
            b.EmpresaId, b.LojaId, b.ErpLoteId,
            b.LastDeviceId, b.LastOperatorName
        )).ToArray());
    }

    [HttpPost("{id}/link")]
    [Authorize]
    public async Task<IActionResult> Link(string id, [FromBody] LinkLoteRequest? req, CancellationToken ct)
    {
        var batch = await db.Set<Batch>().Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id, ct);
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
    [Authorize]
    public async Task<IActionResult> Unlink(string id, CancellationToken ct)
    {
        var batch = await db.Set<Batch>().FirstOrDefaultAsync(b => b.Id == id, ct);
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
