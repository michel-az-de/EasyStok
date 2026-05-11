using System.Security.Claims;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
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
    MobileSaleSyncService saleSync,
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

    /// <summary>
    /// Onda 3 retroativa — para mobile_orders com Status='entregue' e ErpVendaId
    /// ainda nulo, dispara <see cref="MobileSaleSyncService.CreateVendaForDeliveredOrderAsync"/>
    /// em lote. Cenário típico: pedidos chegaram do PWA antes dos mobile_products
    /// terem sido linkados ao ERP (ErpProductId=null), então a Onda 3 do sync
    /// falhou silenciosamente. Após linkagem dos produtos via /api/mobile/products/{id}/link,
    /// chama-se este endpoint pra criar as Vendas que ficaram pendentes.
    ///
    /// Idempotente via <c>Order.ErpVendaId</c> (gate dentro do service).
    /// Pageado: até <c>limit</c> registros por chamada (default 100, max 500).
    /// Resposta inclui <c>hasMore</c> pra cliente saber se precisa repaginar.
    /// </summary>
    [HttpPost("backfill-vendas")]
    public async Task<IActionResult> BackfillVendas(
        [FromQuery] Guid? empresaId,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        var take = Math.Clamp(limit ?? 100, 1, 500);

        var pendentes = await db.Set<Order>().Include(o => o.Items)
            .Where(o => o.EmpresaId == emp
                        && o.Status == "entregue"
                        && o.ErpVendaId == null)
            .OrderBy(o => o.UpdatedAt)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = pendentes.Count > take;
        if (hasMore) pendentes = pendentes.Take(take).ToList();

        var resultados = new List<BackfillVendaItem>(pendentes.Count);
        var criados = 0;
        var semProdutosLinkados = 0;
        var falhas = 0;

        foreach (var order in pendentes)
        {
            var itens = order.Items.Select(i => new OrderItemDto(
                ProductId: i.ProductId,
                Name: i.Name,
                Emoji: i.Emoji,
                Unit: i.Unit,
                Qty: i.Qty,
                UnitPrice: i.UnitPrice
            )).ToList();

            // CreateVendaForDeliveredOrderAsync tem try/catch interno (fail-safe).
            // Retorna true se Venda foi criada, false em qualquer outro caso
            // (já tem ErpVendaId, sem EmpresaId, sem itens linkados, ou exceção).
            // Como pre-filtramos ErpVendaId=null e Status=entregue, false aqui
            // significa "sem produtos linkados ao ERP" (caso esperado).
            var ok = await saleSync.CreateVendaForDeliveredOrderAsync(order, itens, ct);
            if (ok)
            {
                criados++;
                resultados.Add(new BackfillVendaItem(order.Id, "criada", order.ErpVendaId, null));
            }
            else if (order.ErpVendaId.HasValue)
            {
                // Service marcou ErpVendaId mas retornou false — não ocorre no estado atual,
                // defensivo caso a regra mude.
                resultados.Add(new BackfillVendaItem(order.Id, "ja-existia", order.ErpVendaId, null));
            }
            else
            {
                semProdutosLinkados++;
                resultados.Add(new BackfillVendaItem(order.Id, "sem-produtos-linkados-ao-erp", null,
                    "Nenhum item do pedido tem Product.ErpProductId. Linkar os produtos pelo /produtos-mobile e re-rodar."));
            }
        }

        // SaveChanges persiste as Vendas + ErpVendaId em mobile_orders.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Falha no SaveChanges é grave — log e propaga como 500. Não temos
            // como saber quais ficaram "criadas em memória" vs persistidas.
            log.LogError(ex, "Backfill vendas: SaveChanges falhou para empresa {EmpresaId} ({Pendentes} pedidos).",
                emp, pendentes.Count);
            falhas = pendentes.Count;
            return StatusCode(500, new BackfillVendasResponse(
                Processados: pendentes.Count, Criados: 0, SemProdutosLinkados: 0,
                Falhas: falhas, HasMore: hasMore, Mensagem: "SaveChanges falhou: " + ex.Message,
                Resultados: resultados
            ));
        }

        log.LogInformation(
            "Backfill vendas empresa={EmpresaId}: processados={Total} criados={Criados} semLink={SemLink} hasMore={HasMore}",
            emp, pendentes.Count, criados, semProdutosLinkados, hasMore);

        return Ok(new BackfillVendasResponse(
            Processados: pendentes.Count,
            Criados: criados,
            SemProdutosLinkados: semProdutosLinkados,
            Falhas: falhas,
            HasMore: hasMore,
            Mensagem: hasMore ? "Há mais pendentes — chamar de novo." : "Concluído.",
            Resultados: resultados
        ));
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

public record BackfillVendaItem(
    string OrderId,
    string Status,
    Guid? ErpVendaId,
    string? Detalhe
);

public record BackfillVendasResponse(
    int Processados,
    int Criados,
    int SemProdutosLinkados,
    int Falhas,
    bool HasMore,
    string Mensagem,
    IReadOnlyList<BackfillVendaItem> Resultados
);
