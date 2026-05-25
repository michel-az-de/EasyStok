using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Endpoints administrativos do módulo Mobile (Casa da Babá / PWA).
/// Foco em diagnóstico operacional pós-integração: contagens por tenant,
/// pendências de backfill (Vendas, link de produtos, promote de pedidos/lotes),
/// devices pareados e detecção de registros órfãos (sem EmpresaId — útil
/// pra SuperAdmin auditar resquícios pré-Onda-1).
///
/// Tenant guard via <see cref="MobileManagementControllerBase"/>.
/// Read-only — não modifica nada.
/// </summary>
[ApiController]
[Route("api/mobile/admin")]
[Authorize]
public class MobileAdminController(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser) : MobileManagementControllerBase(currentUser)
{
    /// <summary>
    /// Snapshot completo do estado da integração pra um tenant.
    /// Pensado pra ser chamado depois do pareamento + push completo:
    /// confirma que tudo subiu, aponta o que falta linkar/promover, lista
    /// devices ativos e exibe a última atividade.
    ///
    /// Resposta inclui <c>pendentes.vendasParaBackfill</c> — esse é o
    /// número que o operador vai passar pro endpoint
    /// <c>POST /api/mobile/orders/backfill-vendas</c> depois de linkar
    /// os produtos no <c>/produtos-mobile</c>.
    /// </summary>
    [HttpGet("integrity")]
    public async Task<IActionResult> Integrity(
        [FromQuery] Guid? empresaId,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(empresaId, out var emp, out var err)) return err!;

        // ---- Contagens por entidade ----
        var produtosQ = db.Set<Product>().AsNoTracking().Where(p => p.EmpresaId == emp);
        var clientesQ = db.Set<Client>().AsNoTracking().Where(c => c.EmpresaId == emp);
        var pedidosQ = db.Set<Order>().AsNoTracking().Where(o => o.EmpresaId == emp);
        var lotesQ = db.Set<Batch>().AsNoTracking().Where(b => b.EmpresaId == emp);
        var caixaQ = db.Set<CashEntry>().AsNoTracking().Where(c => c.EmpresaId == emp);

        var produtosTotal = await produtosQ.CountAsync(ct);
        var produtosCustom = await produtosQ.CountAsync(p => p.IsCustom, ct);
        var produtosAprovados = await produtosQ.CountAsync(p => p.IsApproved, ct);
        var produtosLinkados = await produtosQ.CountAsync(p => p.ErpProductId != null, ct);
        var produtosCustomPendentes = await produtosQ.CountAsync(
            p => p.IsCustom && !p.IsApproved && p.ErpProductId == null, ct);

        var clientesTotal = await clientesQ.CountAsync(ct);

        var pedidosTotal = await pedidosQ.CountAsync(ct);
        var pedidosPorStatus = await pedidosQ
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var pedidosLinkados = await pedidosQ.CountAsync(o => o.ErpPedidoId != null, ct);
        var vendasParaBackfill = await pedidosQ.CountAsync(
            o => o.Status == "entregue" && o.ErpVendaId == null, ct);
        var pedidosPromovidos = await pedidosQ.CountAsync(o => o.ErpVendaId != null, ct);

        var lotesTotal = await lotesQ.CountAsync(ct);
        var lotesPromovidos = await lotesQ.CountAsync(b => b.ErpLoteId != null, ct);

        var caixaTotal = await caixaQ.CountAsync(ct);

        // ---- Última atividade (max UpdatedAt/CreatedAt) ----
        var ultProduto = await produtosQ.MaxAsync(p => (DateTime?)p.UpdatedAt, ct);
        var ultPedido = await pedidosQ.MaxAsync(o => (DateTime?)o.UpdatedAt, ct);
        var ultLote = await lotesQ.MaxAsync(b => (DateTime?)b.CreatedAt, ct);
        var ultCaixa = await caixaQ.MaxAsync(c => (DateTime?)c.CreatedAt, ct);

        // ---- Devices pareados ----
        var devices = await db.Set<MobileDevice>().AsNoTracking()
            .Where(d => d.EmpresaId == emp)
            .OrderByDescending(d => d.LastSeenAt ?? d.CreatedAt)
            .Select(d => new DeviceBrief(
                d.Id,
                d.Label,
                d.LojaId,
                d.PairedAt,
                d.LastSeenAt,
                d.LastSeenIp,
                d.Revoked,
                d.PairingCode != null
            ))
            .ToListAsync(ct);

        // ---- Backups ----
        var backupsTotal = await db.Set<DeviceBackup>().AsNoTracking()
            .Where(b => b.EmpresaId == emp).CountAsync(ct);
        var ultimoBackup = await db.Set<DeviceBackup>().AsNoTracking()
            .Where(b => b.EmpresaId == emp)
            .MaxAsync(b => (DateTime?)b.CreatedAt, ct);

        // ---- Órfãos (sem EmpresaId) ----
        // Visível só pra SuperAdmin — pra outros usuários, retorna null no campo,
        // já que registros sem tenant podem pertencer a qualquer empresa
        // e expor count entre tenants gera ruído.
        OrphansReport? orfaos = null;
        if (CurrentUser.Nivel == NivelAcesso.SuperAdmin)
        {
            orfaos = new OrphansReport(
                Produtos: await db.Set<Product>().AsNoTracking().CountAsync(p => p.EmpresaId == null, ct),
                Clientes: await db.Set<Client>().AsNoTracking().CountAsync(c => c.EmpresaId == null, ct),
                Pedidos: await db.Set<Order>().AsNoTracking().CountAsync(o => o.EmpresaId == null, ct),
                Lotes: await db.Set<Batch>().AsNoTracking().CountAsync(b => b.EmpresaId == null, ct),
                Caixa: await db.Set<CashEntry>().AsNoTracking().CountAsync(c => c.EmpresaId == null, ct)
            );
        }

        var pedidoStatusEntries = pedidosPorStatus.Select(kv => new StatusCount(kv.Key, kv.Value))
            .OrderBy(s => s.Status).ToArray();

        var resp = new IntegrityReport(
            EmpresaId: emp,
            GeradoEm: DateTime.UtcNow,
            Contagens: new EntityCounts(
                Produtos: new ProductCounts(produtosTotal, produtosCustom, produtosAprovados, produtosLinkados),
                Clientes: clientesTotal,
                Pedidos: new OrderCounts(pedidosTotal, pedidoStatusEntries, pedidosLinkados, pedidosPromovidos),
                Lotes: new BatchCounts(lotesTotal, lotesPromovidos),
                CashEntries: caixaTotal
            ),
            Pendentes: new PendingActions(
                VendasParaBackfill: vendasParaBackfill,
                ProdutosCustomNaoAprovados: produtosCustomPendentes,
                PedidosNaoLinkadosAoErp: pedidosTotal - pedidosLinkados,
                LotesNaoPromovidos: lotesTotal - lotesPromovidos
            ),
            Devices: devices,
            UltimasAtividades: new LastActivities(ultProduto, ultPedido, ultLote, ultCaixa),
            Backups: new BackupsSummary(backupsTotal, ultimoBackup),
            Orfaos: orfaos
        );

        return Ok(resp);
    }
}

// ---------- DTOs ----------

public record IntegrityReport(
    Guid EmpresaId,
    DateTime GeradoEm,
    EntityCounts Contagens,
    PendingActions Pendentes,
    IReadOnlyList<DeviceBrief> Devices,
    LastActivities UltimasAtividades,
    BackupsSummary Backups,
    OrphansReport? Orfaos
);

public record EntityCounts(
    ProductCounts Produtos,
    int Clientes,
    OrderCounts Pedidos,
    BatchCounts Lotes,
    int CashEntries
);

public record ProductCounts(int Total, int Custom, int Aprovados, int LinkadosAoErp);

public record OrderCounts(int Total, IReadOnlyList<StatusCount> PorStatus, int LinkadosAoErpPedido, int ComVendaErp);

public record StatusCount(string Status, int Count);

public record BatchCounts(int Total, int Promovidos);

public record PendingActions(
    int VendasParaBackfill,
    int ProdutosCustomNaoAprovados,
    int PedidosNaoLinkadosAoErp,
    int LotesNaoPromovidos
);

public record DeviceBrief(
    string Id,
    string? Label,
    Guid LojaId,
    DateTime? PairedAt,
    DateTime? LastSeenAt,
    string? LastSeenIp,
    bool Revoked,
    bool PendingPair
);

public record LastActivities(
    DateTime? UltimoProdutoEditado,
    DateTime? UltimoPedidoEditado,
    DateTime? UltimoLoteCriado,
    DateTime? UltimoLancamentoCaixa
);

public record BackupsSummary(int Total, DateTime? UltimoEm);

public record OrphansReport(int Produtos, int Clientes, int Pedidos, int Lotes, int Caixa);
