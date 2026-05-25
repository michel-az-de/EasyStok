using System.Security.Claims;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CriarCliente;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Onda P1 — Revisão e linkagem de clientes mobile↔ERP.
///
/// Auditoria 2026-04-30 (CRITICAL fix tenant): tenant guard via
/// <see cref="MobileManagementControllerBase"/>. Antes, usuário do
/// tenant A passava ?empresaId=&lt;tenant-B&gt; e operava livremente.
/// Agora: usuário comum só opera na própria empresa; SuperAdmin precisa
/// informar empresaId explícito.
/// </summary>
[ApiController]
[Route("api/mobile/clients")]
[Authorize]
public class MobileClientsController(
    EasyStockDbContext db,
    IClienteRepository clienteRepo,
    CriarClienteUseCase criarClienteUseCase,
    ICurrentUserAccessor currentUser,
    ILogger<MobileClientsController> log) : MobileManagementControllerBase(currentUser)
{
    /// <summary>
    /// Lista mobile_clients da empresa. Filtros para o painel:
    /// - <c>pendingOnly</c>: ainda não linkados ao ERP (erp_cliente_id null)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? empresaId,
        [FromQuery] bool? pendingOnly,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var q = db.Set<Client>().AsNoTracking().Where(c => c.EmpresaId == emp);
        if (pendingOnly == true) q = q.Where(c => c.ErpClienteId == null);

        var items = await q.OrderByDescending(c => c.UpdatedAt).Take(500).ToListAsync(ct);

        return Ok(items.Select(c => new MobileClientSummary(
            c.Id, c.Name, c.Apt, c.Address, c.Phone,
            c.OrderCount, c.LastOrder, c.CreatedAt, c.UpdatedAt,
            c.EmpresaId, c.LojaId, c.ErpClienteId, c.ApprovedAt,
            c.LastDeviceId, c.LastOperatorName
        )).ToArray());
    }

    /// <summary>
    /// Linka mobile_client a um <c>Cliente</c> ERP existente.
    /// Se <paramref name="req"/>.<c>ErpClienteId</c> for null, **promove**:
    /// cria um Cliente novo no ERP a partir dos dados do mobile_client e linka.
    /// </summary>
    [HttpPost("{id}/link")]
    public async Task<IActionResult> Link(string id, [FromBody] LinkClienteRequest? req, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        // Tenant-scoped lookup: usuário só vê mobile_clients da própria empresa.
        var mobile = await db.Set<Client>().FirstOrDefaultAsync(c => c.Id == id && c.EmpresaId == emp, ct);
        if (mobile == null) return NotFound();
        if (mobile.EmpresaId == null || mobile.EmpresaId == Guid.Empty)
            return BadRequest(new { error = "mobile_client sem empresa associada" });

        Guid erpClienteId;

        if (req?.ErpClienteId is { } existing && existing != Guid.Empty)
        {
            // Modo 1: linkar a Cliente ERP existente — valida tenant.
            var cliente = await clienteRepo.GetByIdAsync(mobile.EmpresaId.Value, existing);
            if (cliente == null)
                return BadRequest(new { error = "Cliente ERP não encontrado nesta empresa" });
            erpClienteId = cliente.Id;
        }
        else
        {
            // Modo 2: promover mobile_client → criar Cliente ERP e linkar.
            var result = await criarClienteUseCase.ExecuteAsync(new CriarClienteCommand(
                EmpresaId: mobile.EmpresaId.Value,
                Nome: mobile.Name,
                Apt: mobile.Apt,
                Endereco: mobile.Address,
                Telefone: mobile.Phone));
            erpClienteId = result.Id;
        }

        mobile.ErpClienteId = erpClienteId;
        mobile.ApprovedAt = DateTime.UtcNow;
        mobile.ApprovedByUserId = ResolveUserId();
        mobile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Mobile client {MobileId} linkado a Cliente ERP {ErpId} by {UserId}",
            id, erpClienteId, mobile.ApprovedByUserId);
        return Ok(new { erpClienteId });
    }

    /// <summary>Desfaz o link. Cliente ERP permanece — só remove a associação.</summary>
    [HttpPost("{id}/unlink")]
    public async Task<IActionResult> Unlink(string id, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;
        var mobile = await db.Set<Client>().FirstOrDefaultAsync(c => c.Id == id && c.EmpresaId == emp, ct);
        if (mobile == null) return NotFound();

        mobile.ErpClienteId = null;
        mobile.ApprovedAt = null;
        mobile.ApprovedByUserId = null;
        mobile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Mobile client {MobileId} unlink", id);
        return NoContent();
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var g) ? g : null;
    }
}

// ---------- DTOs ----------

public record MobileClientSummary(
    string Id,
    string Name,
    string? Apt,
    string? Address,
    string? Phone,
    int OrderCount,
    DateTime LastOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? EmpresaId,
    Guid? LojaId,
    Guid? ErpClienteId,
    DateTime? ApprovedAt,
    string? LastDeviceId,
    string? LastOperatorName
);

public record LinkClienteRequest(Guid? ErpClienteId);
