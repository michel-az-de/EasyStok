using System.Security.Claims;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CriarPedido;
// IClienteRepository nao usado: cliente eh resolvido via mobile_clients.erp_cliente_id (lookup direto no DbContext).
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Onda P2 — Revisão e linkagem de pedidos mobile↔ERP.
/// Auditoria 2026-04-30: tenant guard via <see cref="MobileManagementControllerBase"/>.
/// </summary>
[ApiController]
[Route("api/mobile/orders")]
[Authorize]
public class MobileOrdersController(
    EasyStockDbContext db,
    IPedidoRepository pedidoRepo,
    CriarPedidoUseCase criarPedidoUseCase,
    ICurrentUserAccessor currentUser,
    ILogger<MobileOrdersController> log) : MobileManagementControllerBase(currentUser)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? empresaId,
        [FromQuery] bool? pendingOnly,
        [FromQuery] string? status,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var q = db.Set<Order>().AsNoTracking().Where(o => o.EmpresaId == emp);
        if (pendingOnly == true) q = q.Where(o => o.ErpPedidoId == null);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(o => o.Status == status);

        var items = await q.OrderByDescending(o => o.UpdatedAt).Take(500).ToListAsync(ct);

        return Ok(items.Select(o => new MobileOrderSummary(
            o.Id, o.ClientId, o.ClientSnapshotName, o.ClientSnapshotRef,
            o.Notes, o.Total, o.Status, o.CreatedAt, o.UpdatedAt,
            o.EmpresaId, o.LojaId, o.ErpPedidoId, o.ErpVendaId,
            o.LastDeviceId, o.LastOperatorName
        )).ToArray());
    }

    /// <summary>
    /// Linka mobile_order a um Pedido ERP existente.
    /// Se erpPedidoId for null, **promove**: cria Pedido ERP novo do mobile_order
    /// (com snapshot do cliente + items) e linka.
    /// </summary>
    [HttpPost("{id}/link")]
    public async Task<IActionResult> Link(string id, [FromBody] LinkPedidoRequest? req, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var mobile = await db.Set<Order>().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.EmpresaId == emp, ct);
        if (mobile == null) return NotFound();
        if (mobile.EmpresaId == null || mobile.EmpresaId == Guid.Empty)
            return BadRequest(new { error = "mobile_order sem empresa associada" });

        Guid erpPedidoId;

        if (req?.ErpPedidoId is { } existing && existing != Guid.Empty)
        {
            // Modo 1: link a Pedido ERP existente — valida tenant.
            var pedido = await pedidoRepo.GetByIdAsync(mobile.EmpresaId.Value, existing);
            if (pedido == null)
                return BadRequest(new { error = "Pedido ERP não encontrado nesta empresa" });
            erpPedidoId = pedido.Id;
        }
        else
        {
            // Modo 2: promove — cria Pedido ERP novo do mobile.

            // Auditoria 2026-04-30 (idempotência): se já existe Pedido ERP
            // com este MobileOrderId, retorna sem duplicar (double-click safe).
            var jaPromovido = await pedidoRepo.FindByMobileOrderIdAsync(mobile.EmpresaId.Value, mobile.Id);
            if (jaPromovido != null)
            {
                mobile.ErpPedidoId = jaPromovido.Id;
                mobile.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                log.LogInformation("Mobile order {MobileId} já promovido a {ErpId} (idempotente).", id, jaPromovido.Id);
                return Ok(new { erpPedidoId = jaPromovido.Id });
            }

            // Resolve cliente via mobile_clients.erp_cliente_id se já houver link.
            Guid? clienteIdResolved = null;
            if (!string.IsNullOrWhiteSpace(mobile.ClientId))
            {
                var mClient = await db.Set<Client>().AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == mobile.ClientId, ct);
                clienteIdResolved = mClient?.ErpClienteId;
            }

            var itens = mobile.Items.Select(i => new CriarPedidoItemInput(
                Nome: i.Name,
                Quantidade: i.Qty,
                PrecoUnitario: i.UnitPrice,
                ProdutoId: null, // produto link reverse fica pra futura iteração
                Emoji: i.Emoji,
                Unidade: i.Unit,
                Observacao: null
            )).ToList();

            var result = await criarPedidoUseCase.ExecuteAsync(new CriarPedidoCommand(
                EmpresaId: mobile.EmpresaId.Value,
                LojaId: mobile.LojaId,
                ClienteId: clienteIdResolved,
                ClienteNomeAdHoc: clienteIdResolved == null ? mobile.ClientSnapshotName : null,
                ClienteAptAdHoc: null,
                ClienteTelefoneAdHoc: null,
                Observacoes: mobile.Notes,
                Origem: "mobile",
                MobileOrderId: mobile.Id,
                Itens: itens,
                CriadoPorUserId: ResolveUserId(),
                CriadoPorNome: mobile.LastOperatorName
            ));
            erpPedidoId = result.Id;
        }

        mobile.ErpPedidoId = erpPedidoId;
        mobile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Mobile order {MobileId} linkado a Pedido ERP {ErpId}.", id, erpPedidoId);
        return Ok(new { erpPedidoId });
    }

    [HttpPost("{id}/unlink")]
    public async Task<IActionResult> Unlink(string id, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;
        var mobile = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == id && o.EmpresaId == emp, ct);
        if (mobile == null) return NotFound();

        mobile.ErpPedidoId = null;
        mobile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Mobile order {MobileId} unlink.", id);
        return NoContent();
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var g) ? g : null;
    }
}

// ---------- DTOs ----------

public record MobileOrderSummary(
    string Id,
    string? ClientId,
    string ClientSnapshotName,
    string? ClientSnapshotRef,
    string? Notes,
    decimal Total,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? EmpresaId,
    Guid? LojaId,
    Guid? ErpPedidoId,
    Guid? ErpVendaId,
    string? LastDeviceId,
    string? LastOperatorName
);

public record LinkPedidoRequest(Guid? ErpPedidoId);
