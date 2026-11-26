using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Caching.Memory;

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
    Linkers.OrderLinker orderLinker,
    IServiceScopeFactory? escopos = null,
    IMemoryCache? cache = null)
{
    private readonly EasyStockDbContext _db = db;
    private readonly IConfiguration _appConfig = appConfig;
    private readonly ILogger<SyncAutoLinker> _log = log;
    private readonly Linkers.CashEntryLinker _cashEntryLinker = cashEntryLinker;
    private readonly Linkers.ClientLinker _clientLinker = clientLinker;
    private readonly Linkers.ProductLinker _productLinker = productLinker;
    private readonly Linkers.BatchLinker _batchLinker = batchLinker;
    private readonly Linkers.OrderLinker _orderLinker = orderLinker;
    private readonly IServiceScopeFactory? _escopos = escopos;
    private readonly IMemoryCache? _cache = cache;

    /// <summary>
    /// Runs auto-link for all entity types after a Push. Reads feature flags from
    /// config (MobileSync:AutoLink:Product/Client/Order/Batch/CashEntry).
    /// Products and Clients are linked first so their ErpIds are available for
    /// Order/Batch/CashEntry promotion.
    /// </summary>
    public async Task RunAsync(
        HashSet<string> productIds, HashSet<string> clientIds,
        HashSet<string> orderIds, HashSet<string> batchIds, HashSet<string> cashIds,
        Guid? empresaId,
        IReadOnlyDictionary<string, string>? documentosDosClientes = null,
        string? deviceId = null)
    {
        var autoLinkProd   = _appConfig.GetValue<bool>("MobileSync:AutoLink:Product", true);
        var autoLinkClient = _appConfig.GetValue<bool>("MobileSync:AutoLink:Client", true);
        var autoLinkOrder  = _appConfig.GetValue<bool>("MobileSync:AutoLink:Order", true);
        var autoLinkBatch  = _appConfig.GetValue<bool>("MobileSync:AutoLink:Batch", true);
        var autoLinkCash   = _appConfig.GetValue<bool>("MobileSync:AutoLink:CashEntry", true);

        if (empresaId is { } empresa && !string.IsNullOrEmpty(deviceId))
            (productIds, clientIds, orderIds) = await ComOrfaosAsync(empresa, deviceId, productIds, clientIds, orderIds);

        if (autoLinkProd   && productIds.Count > 0) await _productLinker.ExecuteAsync(productIds, empresaId);
        if (autoLinkClient && clientIds.Count  > 0) await _clientLinker.ExecuteAsync(clientIds, empresaId, documentosDosClientes);
        // F1/F2/F3 â€” promove orders/batches/cash DEPOIS de products/clients pra que
        // FKs (ErpProductId, ErpClienteId) jÃ¡ estejam preenchidas.
        try
        {
            if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // #2911: cliente que não grava não segura lotes, pedidos e caixa do envio. Os pedidos dele seguem sem
            // a ligação com o Cliente do ERP, e o cliente volta a ser tentado no próximo envio do aparelho.
            _db.DescartarMudancasPendentes();
            _log.LogError(ex, "AutoLink Cliente: gravação dos clientes falhou empresaId={EmpresaId} clients={C} exType={ExType}: {Mensagem}",
                empresaId, clientIds.Count, ex.GetType().Name, ex.Message);
        }
        // #1543: lote antes de pedido. O pedido que chega no mesmo envio do lote so acha estoque para
        // baixar depois que a entrada do lote existe no ERP.
        if (autoLinkBatch  && batchIds.Count   > 0) await _batchLinker.ExecuteAsync(batchIds, empresaId);
        if (autoLinkOrder  && orderIds.Count   > 0) await VincularPedidosAsync(orderIds, empresaId, esperar: true);
        if (autoLinkCash   && cashIds.Count    > 0) await _cashEntryLinker.ExecuteAsync(cashIds, empresaId);
    }

    /// <summary>#3375: chave da trava do Postgres que deixa um vínculo de pedidos por vez em cada empresa.</summary>
    public static string ChaveDaTravaDePedidos(Guid empresaId) => $"mobile-autolink-pedidos:{empresaId:N}";

    /// <summary>#3375: de quanto em quanto tempo a leitura de um aparelho tenta de novo os pedidos órfãos dele.</summary>
    public static readonly TimeSpan IntervaloDaRepescagemNaLeitura = TimeSpan.FromMinutes(5);

    /// <summary>Quantos pedidos órfãos uma leitura tenta de novo: ela é um GET do aparelho e não pode demorar.</summary>
    private const int OrfaosPorLeitura = 10;

    /// <summary>Quanto o envio e o backfill esperam a trava dos pedidos antes de deixá-los para a próxima tentativa.</summary>
    public static readonly TimeSpan EsperaDaTravaDePedidos = TimeSpan.FromSeconds(10);

    private Task<List<string>> PedidosOrfaosAsync(Guid empresaId, string deviceId, int quantos = OrfaosPorEnvio) =>
        _db.Set<Order>().IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.EmpresaId == empresaId && o.LastDeviceId == deviceId && o.ErpPedidoId == null && o.ErpVendaId == null && o.Status != "cancelado")
            .OrderByDescending(o => o.UpdatedAt).Take(quantos).Select(o => o.Id).ToListAsync();

    /// <summary>
    /// #3375: o pedido órfão esperava um envio novo do mesmo aparelho, que pode demorar dias; a leitura acontece o
    /// tempo todo. Tenta de novo só os pedidos, uma vez por intervalo e por aparelho. Com outro vínculo de pedidos
    /// da empresa em andamento, não espera: a próxima leitura tenta.
    /// </summary>
    public async Task RepescarPedidosOrfaosNaLeituraAsync(Guid empresaId, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId) || !_appConfig.GetValue<bool>("MobileSync:AutoLink:Order", true)) return;
        var marca = $"mobile-repesca-leitura:{empresaId:N}:{deviceId}";
        if (_cache?.TryGetValue(marca, out _) == true) return;

        // A marca entra antes da tentativa: uma falha (banco fora, por exemplo) não vira nova tentativa a cada leitura.
        _cache?.Set(marca, true, IntervaloDaRepescagemNaLeitura);
        var orfaos = await PedidosOrfaosAsync(empresaId, deviceId, OrfaosPorLeitura);
        if (orfaos.Count == 0) return;
        if (!await VincularPedidosAsync(orfaos, empresaId, esperar: false))
        {
            _cache?.Remove(marca); // trava ocupada não gasta o intervalo
            return;
        }
        _log.LogInformation("AutoLink: leitura do aparelho tentou de novo órfãos empresa={EmpresaId} orders={O}", empresaId, orfaos.Count);
    }

    /// <summary>
    /// Vincula pedidos com a trava da empresa. A trava é de sessão e fica numa conexão só dela: o vínculo grava em
    /// várias etapas, fora de uma transação única. Devolve false, sem vincular, quando a trava está ocupada: na hora,
    /// se <paramref name="esperar"/> é false, ou depois de <see cref="EsperaDaTravaDePedidos"/>. O pedido que ficou
    /// para trás é órfão e a próxima leitura ou o próximo envio do aparelho o tenta de novo; o que vem depois dos
    /// pedidos no envio (caixa) segue.
    /// </summary>
    private async Task<bool> VincularPedidosAsync(IEnumerable<string> orderIds, Guid? empresaId, bool esperar)
    {
        if (empresaId is not { } empresa || _escopos is null || !_db.Database.IsNpgsql())
        {
            await _orderLinker.ExecuteAsync(orderIds, empresaId);
            return true;
        }

        using var escopo = _escopos.CreateScope();
        var daTrava = escopo.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        await daTrava.Database.OpenConnectionAsync();
        try
        {
            var conexao = daTrava.Database.GetDbConnection();
            var limite = DateTime.UtcNow + (esperar ? EsperaDaTravaDePedidos : TimeSpan.Zero);
            while (!await TravaAsync(conexao, "pg_try_advisory_lock", empresa))
            {
                if (DateTime.UtcNow >= limite)
                {
                    if (esperar) _log.LogWarning("AutoLink Pedido: trava ocupada por {Espera}s, pedidos ficam para a próxima tentativa empresa={EmpresaId}",
                        EsperaDaTravaDePedidos.TotalSeconds, empresa);
                    return false;
                }
                await Task.Delay(100);
            }
            try { await _orderLinker.ExecuteAsync(orderIds, empresaId); }
            finally { await TravaAsync(conexao, "pg_advisory_unlock", empresa); }
            return true;
        }
        finally { await daTrava.Database.CloseConnectionAsync(); }
    }

    private static async Task<bool> TravaAsync(System.Data.Common.DbConnection conexao, string funcao, Guid empresaId)
    {
        await using var comando = conexao.CreateCommand();
        comando.CommandText = $"SELECT {funcao}(hashtextextended(@chave, 0))";
        var chave = comando.CreateParameter(); chave.ParameterName = "chave"; chave.Value = ChaveDaTravaDePedidos(empresaId);
        comando.Parameters.Add(chave);
        return await comando.ExecuteScalarAsync() is true;
    }

    /// <summary>Quantos órfãos de cada tipo um envio tenta de novo, para um órfão que sempre falha não pesar no sync.</summary>
    private const int OrfaosPorEnvio = 50;

    /// <summary>
    /// #2911: produto, cliente e pedido cujo vínculo falhou num envio ficavam só no aparelho; o app não os reenvia e
    /// o backfill não tem quem chame. Cada envio tenta de novo os mais recentes gravados pelo mesmo aparelho.
    /// Pedido.MobileOrderId não é único e a leitura do aparelho também tenta de novo (#3375): quem impede o Pedido
    /// em dobro é a trava de <see cref="VincularPedidosAsync"/>. Pedido cancelado que nunca foi ao ERP fica de fora,
    /// e pedido do fluxo antigo de venda também.
    /// </summary>
    private async Task<(HashSet<string>, HashSet<string>, HashSet<string>)> ComOrfaosAsync(
        Guid empresaId, string deviceId, HashSet<string> productIds, HashSet<string> clientIds, HashSet<string> orderIds)
    {
        var produtos = await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.LastDeviceId == deviceId && p.ErpProductId == null)
            .OrderByDescending(p => p.UpdatedAt).Take(OrfaosPorEnvio).Select(p => p.Id).ToListAsync();
        var clientes = await _db.Set<Client>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.LastDeviceId == deviceId && c.ErpClienteId == null)
            .OrderByDescending(c => c.UpdatedAt).Take(OrfaosPorEnvio).Select(c => c.Id).ToListAsync();
        var pedidos = await PedidosOrfaosAsync(empresaId, deviceId);

        if (produtos.Count + clientes.Count + pedidos.Count > 0)
            _log.LogInformation("AutoLink: tentando de novo órfãos empresa={EmpresaId} products={P} clients={C} orders={O}",
                empresaId, produtos.Count, clientes.Count, pedidos.Count);

        return (Uniao(productIds, produtos), Uniao(clientIds, clientes), Uniao(orderIds, pedidos));

        static HashSet<string> Uniao(HashSet<string> doEnvio, List<string> orfaos) =>
            new(doEnvio.Concat(orfaos), StringComparer.Ordinal);
    }

    /// <summary>
    /// F5â€” Backfill: iterates all unlinked entities for the empresa and runs
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
        // #2813: o arquivo do histórico importado (NaoPromoverEm) não é pendência de promoção ao caixa.
        var cashIds = await _db.Set<CashEntry>().IgnoreQueryFilters()
            .Where(c => c.EmpresaId == empresaId && c.ErpMovimentoCaixaId == null && c.NaoPromoverEm == null)
            .Select(c => c.Id).ToListAsync(ct);

        _log.LogInformation("Backfill empresa={EmpresaId}: products={P} clients={C} orders={O} batches={B} cash={CE}",
            empresaId, productIds.Count, clientIds.Count, orderIds.Count, batchIds.Count, cashIds.Count);

        await _productLinker.ExecuteAsync(productIds, empresaId);
        await _clientLinker.ExecuteAsync(clientIds, empresaId);
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
        await _batchLinker.ExecuteAsync(batchIds, empresaId);
        await VincularPedidosAsync(orderIds, empresaId, esperar: true);
        await _cashEntryLinker.ExecuteAsync(cashIds, empresaId);

        return new BackfillCounts(productIds.Count, clientIds.Count, orderIds.Count, batchIds.Count, cashIds.Count);
    }

    // â”€â”€â”€ F0: auto-link Product â†” Produto / Client â†” Cliente â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

}

/// <summary>Counts returned by BackfillAsync for the HTTP response.</summary>
public record BackfillCounts(int Products, int Clients, int Orders, int Batches, int CashEntries);
