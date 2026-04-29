using System.Security.Claims;
using EasyStock.Api.Mobile.Security;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Onda 4 — Painel "Operação" no web + entrega de comandos pro PWA.
///
/// Dois lados:
///   - GET /api/mobile/operation/dashboard [Authorize] — KPIs do dia da loja
///     (vendas, pedidos abertos, lotes ativos, saldo de caixa, divergências
///     de estoque, devices ativos). Usado por /operacao no painel web.
///   - GET /api/mobile/operation/pending-commands [MobileApiKey] — device
///     puxa comandos pendentes na próxima requisição. Servidor marca como
///     entregue e zera fila.
/// </summary>
[ApiController]
[Route("api/mobile/operation")]
public class OperationController(
    EasyStockDbContext db,
    ILogger<OperationController> log) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly ILogger<OperationController> _log = log;

    /// <summary>
    /// Dashboard ao vivo da loja — KPIs agregados em tempo de request.
    /// Cache curto (10s) seria bom em produção; por ora é always-fresh.
    /// </summary>
    [HttpGet("dashboard")]
    [Authorize]
    public async Task<ActionResult<OperationDashboard>> GetDashboard(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        CancellationToken ct)
    {
        if (empresaId == Guid.Empty) return BadRequest(new { error = "empresaId obrigatório" });

        var todayStart = DateTime.UtcNow.Date;

        // Pedidos abertos (não entregue/cancelado)
        var openOrdersQ = _db.Set<Order>().AsNoTracking()
            .Where(o => o.EmpresaId == empresaId &&
                        o.Status != "entregue" && o.Status != "cancelado");
        if (lojaId.HasValue) openOrdersQ = openOrdersQ.Where(o => o.LojaId == lojaId);
        var openOrders = await openOrdersQ.ToListAsync(ct);

        // Pedidos entregues hoje (vendas do dia, somatório)
        var deliveredQ = _db.Set<Order>().AsNoTracking()
            .Where(o => o.EmpresaId == empresaId &&
                        o.Status == "entregue" &&
                        o.UpdatedAt >= todayStart);
        if (lojaId.HasValue) deliveredQ = deliveredQ.Where(o => o.LojaId == lojaId);
        var delivered = await deliveredQ.ToListAsync(ct);

        // Lotes do dia
        var batchesQ = _db.Set<Batch>().AsNoTracking()
            .Where(b => b.EmpresaId == empresaId && b.CreatedAt >= todayStart);
        if (lojaId.HasValue) batchesQ = batchesQ.Where(b => b.LojaId == lojaId);
        var batches = await batchesQ.CountAsync(ct);

        // Saldo de caixa do dia (vendas entregues + entradas extras − saídas)
        var cashEntriesQ = _db.Set<CashEntry>().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.CreatedAt >= todayStart);
        if (lojaId.HasValue) cashEntriesQ = cashEntriesQ.Where(c => c.LojaId == lojaId);
        var cashEntries = await cashEntriesQ.ToListAsync(ct);

        var cashIn = cashEntries.Where(c => c.Type == "income").Sum(c => c.Amount);
        var cashOut = cashEntries.Where(c => c.Type == "expense").Sum(c => c.Amount);
        var sold = delivered.Sum(o => o.Total);
        var saldo = sold + cashIn - cashOut;

        // Devices ativos (last seen <30min)
        var devicesActiveSince = DateTime.UtcNow.AddMinutes(-30);
        var devicesQ = _db.Set<MobileDevice>().AsNoTracking()
            .Where(d => d.EmpresaId == empresaId && !d.Revoked);
        if (lojaId.HasValue) devicesQ = devicesQ.Where(d => d.LojaId == lojaId);
        var devices = await devicesQ.ToListAsync(ct);
        var activeDevices = devices.Count(d => d.LastSeenAt.HasValue && d.LastSeenAt >= devicesActiveSince);

        // Pedidos travados (preparando há >30min) — UX premium do app já tem isso
        var stuckThreshold = DateTime.UtcNow.AddMinutes(-30);
        var stuck = openOrders.Count(o => o.Status == "preparando" &&
                                          (o.UpdatedAt < stuckThreshold || o.CreatedAt < stuckThreshold));

        // Conferência pendente (pronto sem confirmedAt)
        var conferPending = openOrders.Count(o => o.Status == "pronto" && o.ConfirmedAt == null);

        // Divergências de estoque (linkados onde mobile.Stock != ERP.Quantidade)
        // Faz uma estimativa rápida — full check fica em /produtos-mobile/divergencias
        var linkedProducts = await _db.Set<Product>().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.ErpProductId != null)
            .Select(p => new { p.Id, p.Stock, ErpId = p.ErpProductId!.Value, p.LojaId })
            .ToListAsync(ct);

        var produtoIds = linkedProducts.Select(p => p.ErpId).Distinct().ToList();
        var itensEstoque = produtoIds.Count == 0
            ? new List<ItemEstoque>()
            : await _db.Set<ItemEstoque>().AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && produtoIds.Contains(i.ProdutoId))
                .ToListAsync(ct);

        var divergences = linkedProducts.Count(p =>
        {
            var item = itensEstoque.FirstOrDefault(i =>
                i.ProdutoId == p.ErpId &&
                (i.LojaId == null || i.LojaId == p.LojaId));
            var erpStock = item?.QuantidadeAtual?.Value ?? 0;
            return p.Stock != erpStock;
        });

        // Produtos custom pendentes de aprovação
        var pendingApproval = await _db.Set<Product>().AsNoTracking()
            .CountAsync(p => p.EmpresaId == empresaId &&
                             p.IsCustom && !p.IsApproved && p.ErpProductId == null, ct);

        return Ok(new OperationDashboard(
            EmpresaId: empresaId,
            LojaId: lojaId,
            Generated: DateTime.UtcNow,
            // Vendas
            VendasHojeValor: sold,
            VendasHojeCount: delivered.Count,
            CaixaSaldoHoje: saldo,
            CaixaEntradasExtras: cashIn,
            CaixaSaidas: cashOut,
            // Pedidos
            PedidosAbertos: openOrders.Count,
            PedidosPreparando: openOrders.Count(o => o.Status == "preparando"),
            PedidosProntos: openOrders.Count(o => o.Status == "pronto"),
            PedidosTravados: stuck,
            ConferenciaPendente: conferPending,
            // Producao
            LotesHoje: batches,
            // Catalogo
            DivergenciasEstoque: divergences,
            ProdutosPendenteAprovacao: pendingApproval,
            // Devices
            DevicesAtivos: activeDevices,
            DevicesTotal: devices.Count
        ));
    }

    /// <summary>
    /// Device puxa comandos pendentes (Onda 4). Marca como entregue e
    /// retorna lista. Tipos: flush_now, pull_now, reload, message.
    ///
    /// Endpoint protegido por <see cref="MobileApiKeyAttribute"/> — só
    /// devices pareados podem puxar seus próprios comandos.
    /// </summary>
    [HttpGet("pending-commands")]
    [MobileApiKey]
    [AllowAnonymous] // permite legado sem header (modo transição)
    public async Task<ActionResult<DeviceCommandDto[]>> GetPendingCommands(CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device == null) return Ok(Array.Empty<DeviceCommandDto>()); // legado/anônimo

        var now = DateTime.UtcNow;
        var pending = await _db.Set<DeviceCommand>()
            .Where(c => c.DeviceId == device.Id &&
                        c.DeliveredAt == null &&
                        (c.ExpiresAt == null || c.ExpiresAt > now))
            .OrderBy(c => c.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        if (pending.Count == 0) return Ok(Array.Empty<DeviceCommandDto>());

        // Marca como entregue (ack-on-pull). Se app crashar antes de
        // executar, comando perdido — aceitável pra esses tipos
        // idempotentes (flush/pull/reload).
        foreach (var c in pending) c.DeliveredAt = now;
        await _db.SaveChangesAsync(ct);

        return Ok(pending.Select(c => new DeviceCommandDto(
            c.Id, c.CommandType, c.PayloadJson, c.CreatedAt
        )).ToArray());
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue("sub")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var g) ? g : null;
    }
}

// ---------- DTOs ----------

/// <summary>KPIs agregados do painel /operacao.</summary>
public record OperationDashboard(
    Guid EmpresaId,
    Guid? LojaId,
    DateTime Generated,
    decimal VendasHojeValor,
    int VendasHojeCount,
    decimal CaixaSaldoHoje,
    decimal CaixaEntradasExtras,
    decimal CaixaSaidas,
    int PedidosAbertos,
    int PedidosPreparando,
    int PedidosProntos,
    int PedidosTravados,
    int ConferenciaPendente,
    int LotesHoje,
    int DivergenciasEstoque,
    int ProdutosPendenteAprovacao,
    int DevicesAtivos,
    int DevicesTotal
);

/// <summary>Item retornado no pull de comandos (PWA processa).</summary>
public record DeviceCommandDto(Guid Id, string CommandType, string? PayloadJson, DateTime CreatedAt);
