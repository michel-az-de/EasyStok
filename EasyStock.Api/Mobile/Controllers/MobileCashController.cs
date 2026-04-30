using System.Security.Claims;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Onda P3 — Revisão e linkagem de lançamentos de caixa mobile↔ERP.
///
/// Espelha <see cref="MobileClientsController"/>/<see cref="MobileOrdersController"/>:
/// gestor revisa entradas/saídas registradas no app pelo operador e pode
/// linkar a um <c>MovimentoCaixa</c> existente OU **promover** criando
/// um movimento ERP novo a partir do <c>mobile_cash_entries</c>.
/// </summary>
[ApiController]
[Route("api/mobile/cash")]
public class MobileCashController(
    EasyStockDbContext db,
    RegistrarMovimentoCaixaUseCase registrarMovUseCase,
    ILogger<MobileCashController> log) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<MobileCashSummary[]>> List(
        [FromQuery] Guid empresaId,
        [FromQuery] bool? pendingOnly,
        CancellationToken ct)
    {
        if (empresaId == Guid.Empty) return BadRequest(new { error = "empresaId obrigatório" });

        var q = db.Set<CashEntry>().AsNoTracking().Where(c => c.EmpresaId == empresaId);
        if (pendingOnly == true) q = q.Where(c => c.ErpMovimentoCaixaId == null);

        var items = await q.OrderByDescending(c => c.CreatedAt).Take(500).ToListAsync(ct);

        return Ok(items.Select(c => new MobileCashSummary(
            c.Id, c.Type, c.Amount, c.Description, c.CreatedAt,
            c.EmpresaId, c.LojaId, c.ErpMovimentoCaixaId,
            c.LastDeviceId, c.LastOperatorName
        )).ToArray());
    }

    /// <summary>
    /// Promove mobile cash entry → cria MovimentoCaixa no ERP e linka.
    /// (Não há "modo link a existente" porque mobile_cash_entry é único e
    /// tem que ser representado 1:1 no ERP — sempre promove se não estiver linkado.)
    /// </summary>
    [HttpPost("{id}/promover")]
    [Authorize]
    public async Task<IActionResult> Promover(string id, CancellationToken ct)
    {
        var entry = await db.Set<CashEntry>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entry == null) return NotFound();
        if (entry.EmpresaId == null || entry.EmpresaId == Guid.Empty)
            return BadRequest(new { error = "mobile_cash_entry sem empresa associada" });
        if (entry.ErpMovimentoCaixaId.HasValue)
            return BadRequest(new { error = "Já está linkado a um movimento ERP." });

        // expense -> saida, income -> entrada
        var tipo = entry.Type?.ToLowerInvariant() == "income" ? "entrada" : "saida";

        var result = await registrarMovUseCase.ExecuteAsync(new RegistrarMovimentoCaixaCommand(
            EmpresaId: entry.EmpresaId.Value,
            Tipo: tipo,
            Valor: entry.Amount,
            Descricao: entry.Description,
            LojaId: entry.LojaId,
            DataMovimento: entry.CreatedAt,
            RegistradoPorUserId: ResolveUserId(),
            RegistradoPorNome: entry.LastOperatorName,
            Origem: "mobile"));

        entry.ErpMovimentoCaixaId = result.Id;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Mobile cash {MobileId} promovido a Movimento {ErpId} ({Tipo}).", id, result.Id, tipo);
        return Ok(new { erpMovimentoCaixaId = result.Id });
    }

    [HttpPost("{id}/unlink")]
    [Authorize]
    public async Task<IActionResult> Unlink(string id, CancellationToken ct)
    {
        var entry = await db.Set<CashEntry>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entry == null) return NotFound();

        entry.ErpMovimentoCaixaId = null;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Mobile cash {Id} unlink.", id);
        return NoContent();
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var g) ? g : null;
    }
}

public record MobileCashSummary(
    string Id,
    string Type,
    decimal Amount,
    string Description,
    DateTime CreatedAt,
    Guid? EmpresaId,
    Guid? LojaId,
    Guid? ErpMovimentoCaixaId,
    string? LastDeviceId,
    string? LastOperatorName
);
