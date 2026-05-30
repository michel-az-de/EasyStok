using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Auto-links mobile entities (Product/Client/Order/Batch/CashEntry) to their ERP
/// counterparts after mutations are saved. Extracted from SyncController.
/// All link methods are idempotent and fail-safe (exceptions are caught + logged).
/// </summary>
public class SyncAutoLinker(
    EasyStockDbContext db,
    IConfiguration appConfig,
    ILogger<SyncAutoLinker> log,
    Linkers.CashEntryLinker cashEntryLinker,
    Linkers.ClientLinker clientLinker,
    Linkers.ProductLinker productLinker,
    Linkers.BatchLinker batchLinker,
    Linkers.OrderLinker orderLinker)
{
    private readonly EasyStockDbContext _db = db;
    private readonly IConfiguration _appConfig = appConfig;
    private readonly ILogger<SyncAutoLinker> _log = log;
    private readonly Linkers.CashEntryLinker _cashEntryLinker = cashEntryLinker;
    private readonly Linkers.ClientLinker _clientLinker = clientLinker;
    private readonly Linkers.ProductLinker _productLinker = productLinker;
    private readonly Linkers.BatchLinker _batchLinker = batchLinker;
    private readonly Linkers.OrderLinker _orderLinker = orderLinker;

    /// <summary>
    /// Runs auto-link for all entity types after a Push. Reads feature flags from
    /// config (MobileSync:AutoLink:Product/Client/Order/Batch/CashEntry).
    /// Products and Clients are linked first so their ErpIds are available for
    /// Order/Batch/CashEntry promotion.
    /// </summary>
    public async Task RunAsync(
        HashSet<string> productIds, HashSet<string> clientIds,
        HashSet<string> orderIds, HashSet<string> batchIds, HashSet<string> cashIds,
        Guid? empresaId)
    {
        var autoLinkProd   = _appConfig.GetValue<bool>("MobileSync:AutoLink:Product", true);
        var autoLinkClient = _appConfig.GetValue<bool>("MobileSync:AutoLink:Client", true);
        var autoLinkOrder  = _appConfig.GetValue<bool>("MobileSync:AutoLink:Order", true);
        var autoLinkBatch  = _appConfig.GetValue<bool>("MobileSync:AutoLink:Batch", true);
        var autoLinkCash   = _appConfig.GetValue<bool>("MobileSync:AutoLink:CashEntry", true);

        if (autoLinkProd   && productIds.Count > 0) await _productLinker.ExecuteAsync(productIds, empresaId);
        if (autoLinkClient && clientIds.Count  > 0) await _clientLinker.ExecuteAsync(clientIds, empresaId);
        // F1/F2/F3 â€” promove orders/batches/cash DEPOIS de products/clients pra que
        // FKs (ErpProductId, ErpClienteId) jÃ¡ estejam preenchidas.
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
        if (autoLinkOrder  && orderIds.Count   > 0) await _orderLinker.ExecuteAsync(orderIds, empresaId);
        if (autoLinkBatch  && batchIds.Count   > 0) await _batchLinker.ExecuteAsync(batchIds, empresaId);
        if (autoLinkCash   && cashIds.Count    > 0) await _cashEntryLinker.ExecuteAsync(cashIds, empresaId);
    }

    /// <summary>
    /// F5 â€” Backfill: iterates all unlinked entities for the empresa and runs
    /// the full auto-link pipeline. Idempotent.
    /// </summary>
    public async Task<BackfillCounts> BackfillAsync(Guid empresaId, CancellationToken ct)
    {
        var productIds = await _db.Set<Product>().IgnoreQueryFilters()
            .Where(p => p.EmpresaId == empresaId && p.ErpProductId == null)
            .Select(p => p.Id).ToListAsync(ct);
        var clientIds = await _db.Set<Client>().IgnoreQueryFilters()
            .Where(c => c.EmpresaId == empresaId && c.ErpClienteId == null)
            .Select(c => c.Id).ToListAsync(ct);
        // F8: processa TODOS os orders (mesmo jÃ¡ promovidos) â€” idempotencia interna detecta.
        var orderIds = await _db.Set<Order>().IgnoreQueryFilters()
            .Where(o => o.EmpresaId == empresaId)
            .Select(o => o.Id).ToListAsync(ct);
        var batchIds = await _db.Set<Batch>().IgnoreQueryFilters()
            .Where(b => b.EmpresaId == empresaId && b.ErpLoteId == null)
            .Select(b => b.Id).ToListAsync(ct);
        var cashIds = await _db.Set<CashEntry>().IgnoreQueryFilters()
            .Where(c => c.EmpresaId == empresaId && c.ErpMovimentoCaixaId == null)
            .Select(c => c.Id).ToListAsync(ct);

        _log.LogInformation("Backfill empresa={EmpresaId}: products={P} clients={C} orders={O} batches={B} cash={CE}",
            empresaId, productIds.Count, clientIds.Count, orderIds.Count, batchIds.Count, cashIds.Count);

        await _productLinker.ExecuteAsync(productIds, empresaId);
        await _clientLinker.ExecuteAsync(clientIds, empresaId);
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
        await _orderLinker.ExecuteAsync(orderIds, empresaId);
        await _batchLinker.ExecuteAsync(batchIds, empresaId);
        await _cashEntryLinker.ExecuteAsync(cashIds, empresaId);

        return new BackfillCounts(productIds.Count, clientIds.Count, orderIds.Count, batchIds.Count, cashIds.Count);
    }

    // â”€â”€â”€ F0: auto-link Product â†” Produto / Client â†” Cliente â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

}

/// <summary>Counts returned by BackfillAsync for the HTTP response.</summary>
public record BackfillCounts(int Products, int Clients, int Orders, int Batches, int CashEntries);
