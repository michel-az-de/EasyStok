using EasyStock.Api.Mobile.DTOs;
using EasyStock.Application.UseCases.CriarPedido;
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
    IPedidoRepository pedidoRepo,
    ILoteRepository loteRepo,
    MobileStockReconciler stockReconciler,
    MobileSaleSyncService saleSync,
    CriarPedidoUseCase criarPedidoUseCase,
    MobileSystemUserResolver systemUserResolver,
    IConfiguration appConfig,
    ILogger<SyncAutoLinker> log,
    Linkers.CashEntryLinker cashEntryLinker,
    Linkers.ClientLinker clientLinker,
    Linkers.ProductLinker productLinker,
    Linkers.BatchLinker batchLinker)
{
    private readonly EasyStockDbContext _db = db;
    private readonly IPedidoRepository _pedidoRepo = pedidoRepo;
    private readonly ILoteRepository _loteRepo = loteRepo;
    private readonly MobileStockReconciler _stockReconciler = stockReconciler;
    private readonly MobileSaleSyncService _saleSync = saleSync;
    private readonly CriarPedidoUseCase _criarPedidoUseCase = criarPedidoUseCase;
    private readonly MobileSystemUserResolver _systemUserResolver = systemUserResolver;
    private readonly IConfiguration _appConfig = appConfig;
    private readonly ILogger<SyncAutoLinker> _log = log;
    private readonly Linkers.CashEntryLinker _cashEntryLinker = cashEntryLinker;
    private readonly Linkers.ClientLinker _clientLinker = clientLinker;
    private readonly Linkers.ProductLinker _productLinker = productLinker;
    private readonly Linkers.BatchLinker _batchLinker = batchLinker;

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
        if (autoLinkOrder  && orderIds.Count   > 0) await TryAutoLinkOrdersAsync(orderIds, empresaId);
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
        await TryAutoLinkOrdersAsync(orderIds, empresaId);
        await _batchLinker.ExecuteAsync(batchIds, empresaId);
        await _cashEntryLinker.ExecuteAsync(cashIds, empresaId);

        return new BackfillCounts(productIds.Count, clientIds.Count, orderIds.Count, batchIds.Count, cashIds.Count);
    }

    // â”€â”€â”€ F0: auto-link Product â†” Produto / Client â†” Cliente â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task TryAutoLinkOrdersAsync(IEnumerable<string> mobileOrderIds, Guid? empresaId)
    {
        var idsList = mobileOrderIds as ICollection<string> ?? mobileOrderIds.ToList();
        if (!empresaId.HasValue)
        {
            _log.LogWarning(
                "AutoLink Pedido SKIPPED: device nao pareado (empresaId=null), {Count} pedidos ficam orfaos em mobile_orders",
                idsList.Count);
            return;
        }
        var processed = 0;
        var errorSkip = 0;
        foreach (var oid in idsList)
        {
            try
            {
                var mobileO = await _db.Set<Order>().IgnoreQueryFilters()
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == oid && o.EmpresaId == empresaId);
                if (mobileO == null) continue;
                processed++;

                Guid? pedidoIdResolvido = null;

                if (mobileO.ErpPedidoId.HasValue && mobileO.ErpPedidoId.Value != Guid.Empty)
                {
                    pedidoIdResolvido = mobileO.ErpPedidoId.Value;
                }
                else
                {
                    var jaPromovido = await _pedidoRepo.FindByMobileOrderIdAsync(empresaId.Value, mobileO.Id);
                    if (jaPromovido != null)
                    {
                        mobileO.ErpPedidoId = jaPromovido.Id;
                        mobileO.UpdatedAt = DateTime.UtcNow;
                        pedidoIdResolvido = jaPromovido.Id;
                        _log.LogInformation("AutoLink Pedido (idempotente MobileOrderId): mobile={MobileId} â†’ erp={ErpId}", oid, jaPromovido.Id);
                    }
                    // F6 idempotencia: pull webâ†’mobile retornou Pedido web com Guid,
                    // APK reenfileirou de volta com mobile.Id=Guid.
                    else if (Guid.TryParse(mobileO.Id, out var pedidoIdAsGuid))
                    {
                        var pedidoExistente = await _pedidoRepo.GetByIdAsync(empresaId.Value, pedidoIdAsGuid);
                        if (pedidoExistente != null)
                        {
                            mobileO.ErpPedidoId = pedidoExistente.Id;
                            mobileO.UpdatedAt = DateTime.UtcNow;
                            if (string.IsNullOrEmpty(pedidoExistente.MobileOrderId))
                            {
                                pedidoExistente.MobileOrderId = mobileO.Id;
                                await _pedidoRepo.UpdateAsync(pedidoExistente);
                            }
                            pedidoIdResolvido = pedidoExistente.Id;
                            _log.LogInformation("AutoLink Pedido (idempotente Guid eco): mobile={MobileId} â†” erp={ErpId}",
                                oid, pedidoExistente.Id);
                        }
                    }

                    if (pedidoIdResolvido == null)
                    {
                        Client? mClient = null;
                        if (!string.IsNullOrWhiteSpace(mobileO.ClientId))
                        {
                            mClient = await _db.Set<Client>().IgnoreQueryFilters().AsNoTracking()
                                .FirstOrDefaultAsync(c => c.Id == mobileO.ClientId);
                        }

                        var itens = mobileO.Items.Select(i => new CriarPedidoItemInput(
                            Nome: i.Name,
                            Quantidade: i.Qty,
                            PrecoUnitario: i.UnitPrice,
                            ProdutoId: null,
                            Emoji: i.Emoji,
                            Unidade: i.Unit,
                            Observacao: null
                        )).ToList();

                        var clienteNomeFinal = !string.IsNullOrWhiteSpace(mobileO.ClientSnapshotName)
                            ? mobileO.ClientSnapshotName
                            : (mClient?.Name ?? "Avulso");
                        var result = await _criarPedidoUseCase.ExecuteAsync(new CriarPedidoCommand(
                            EmpresaId: empresaId.Value,
                            LojaId: mobileO.LojaId,
                            ClienteId: null,
                            ClienteNomeAdHoc: clienteNomeFinal,
                            ClienteAptAdHoc: mClient?.Apt,
                            ClienteTelefoneAdHoc: mClient?.Phone,
                            Observacoes: mobileO.Notes,
                            Origem: "mobile",
                            MobileOrderId: mobileO.Id,
                            Itens: itens,
                            CriadoPorUserId: null,
                            CriadoPorNome: mobileO.LastOperatorName,
                            AgendadoParaEm: mobileO.ScheduledDeliveryAt
                        ));

                        try
                        {
                            Guid? clienteFk = null;
                            if (mClient?.ErpClienteId.HasValue == true)
                            {
                                var existeErp = await _db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
                                    .AnyAsync(c => c.Id == mClient.ErpClienteId.Value && c.EmpresaId == empresaId);
                                if (existeErp) clienteFk = mClient.ErpClienteId;
                            }
                            if (clienteFk.HasValue)
                            {
                                await _db.Set<Pedido>().IgnoreQueryFilters()
                                    .Where(p => p.Id == result.Id)
                                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ClienteId, clienteFk));
                            }

                            var productIds = mobileO.Items.Where(i => !string.IsNullOrWhiteSpace(i.ProductId))
                                .Select(i => i.ProductId).Distinct().ToList();
                            var produtoMap = await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                                .Where(p => productIds.Contains(p.Id) && p.EmpresaId == empresaId)
                                .ToDictionaryAsync(p => p.Id, p => p.ErpProductId);

                            var pedidoItens = await _db.Set<PedidoItem>().IgnoreQueryFilters()
                                .Where(pi => pi.PedidoId == result.Id)
                                .OrderBy(pi => pi.Id).ToListAsync();
                            for (int idx = 0; idx < Math.Min(pedidoItens.Count, mobileO.Items.Count); idx++)
                            {
                                var mobItem = mobileO.Items[idx];
                                if (!string.IsNullOrWhiteSpace(mobItem.ProductId)
                                    && produtoMap.TryGetValue(mobItem.ProductId, out var prodFk)
                                    && prodFk.HasValue)
                                {
                                    pedidoItens[idx].ProdutoId = prodFk;
                                }
                            }
                            if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
                        }
                        catch (Exception linkEx)
                        {
                            _log.LogWarning(linkEx, "Pedido {ErpId}: falha ao linkar Cliente/Produto FKs (pedido criado mas ad-hoc). {Msg}",
                                result.Id, linkEx.Message);
                        }

                        mobileO.ErpPedidoId = result.Id;
                        mobileO.UpdatedAt = DateTime.UtcNow;
                        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
                        pedidoIdResolvido = result.Id;
                        _log.LogInformation("AutoLink Pedido CRIADO: mobile={MobileId} â†’ erp={ErpId} status={Status}",
                            oid, result.Id, mobileO.Status);
                    }
                }

                // F8-F: sincroniza Status do Pedido com mobileO.Status SEMPRE.
                if (pedidoIdResolvido.HasValue)
                {
                    await EnsureStatusSyncAsync(pedidoIdResolvido.Value, mobileO);
                    await EnsureClienteLinkAsync(pedidoIdResolvido.Value, mobileO);
                }

                // F7-A â€” Pagamento auto quando mobileO.Status == "entregue".
                if (pedidoIdResolvido.HasValue
                    && string.Equals(mobileO.Status, "entregue", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsurePagamentoEntregueAsync(pedidoIdResolvido.Value, mobileO);
                    await EnsureVendaAsync(mobileO);
                    await EnsureStockSaidaAsync(mobileO);
                }
            }
            catch (Exception ex)
            {
                errorSkip++;
                _log.LogError(ex,
                    "AutoLink Pedido FALHOU mobile={MobileId} empresaId={EmpresaId} exType={ExType}: {Mensagem}",
                    oid, empresaId, ex.GetType().Name, ex.Message);
            }
        }
        _log.LogInformation(
            "AutoLink Pedido summary empresaId={EmpresaId} total={Total} processed={Processed} errors={Errors}",
            empresaId, idsList.Count, processed, errorSkip);
    }

    /// <summary>
    /// F8-F â€” sincroniza Status do Pedido web com mobileO.Status. Idempotente.
    /// </summary>
    private async Task EnsureStatusSyncAsync(Guid pedidoId, Order mobileO)
    {
        var alvo = (mobileO.Status ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(alvo)) return;
        var validos = new HashSet<string> { "aguardando", "preparando", "pronto", "entregue", "cancelado" };
        if (!validos.Contains(alvo)) return;
        try
        {
            var rows = await _db.Set<Pedido>().IgnoreQueryFilters()
                .Where(p => p.Id == pedidoId && p.Status != alvo)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, alvo)
                    .SetProperty(p => p.AlteradoEm, DateTime.UtcNow)
                    .SetProperty(p => p.EntreguEm, p =>
                        alvo == "entregue" ? (DateTime?)mobileO.UpdatedAt : p.EntreguEm)
                    .SetProperty(p => p.CanceladoEm, p =>
                        alvo == "cancelado" ? (DateTime?)mobileO.UpdatedAt : p.CanceladoEm));
            if (rows > 0)
                _log.LogInformation("F8-F Status sincronizado: pedido={ErpId} â†’ {Status}", pedidoId, alvo);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "F8-F Status sync falhou pedido={ErpId}: {Msg}", pedidoId, ex.Message);
        }
    }

    /// <summary>
    /// F8-G â€” cria Venda quando pedido entregue. Idempotente via mobileO.ErpVendaId.
    /// </summary>
    private async Task EnsureVendaAsync(Order mobileO)
    {
        try
        {
            var trackedOrder = await _db.Set<Order>().IgnoreQueryFilters()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == mobileO.Id);
            if (trackedOrder == null) return;

            if (!trackedOrder.ErpVendaId.HasValue)
            {
                var created = await _saleSync.CreateVendaForDeliveredOrderAsync(trackedOrder, trackedOrder.Items.Select(i => new OrderItemDto(
                    ProductId: i.ProductId ?? "",
                    Name: i.Name,
                    Emoji: i.Emoji,
                    Unit: i.Unit,
                    Qty: i.Qty,
                    UnitPrice: i.UnitPrice
                )).ToList());
                if (created && _db.ChangeTracker.HasChanges())
                    await _db.SaveChangesAsync();
            }

            // F9-A: popula Pedido.VendaId retroativamente.
            if (trackedOrder.ErpVendaId.HasValue && trackedOrder.ErpPedidoId.HasValue)
            {
                await _db.Set<Pedido>().IgnoreQueryFilters()
                    .Where(p => p.Id == trackedOrder.ErpPedidoId.Value && p.VendaId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.VendaId, trackedOrder.ErpVendaId.Value));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "F8-G Venda falhou pedido_mobile={MobileId}: {Msg}", mobileO.Id, ex.Message);
        }
    }

    /// <summary>
    /// F9-B: re-linka Pedido.ClienteId via match por nome. Idempotente.
    /// </summary>
    private async Task EnsureClienteLinkAsync(Guid pedidoId, Order mobileO)
    {
        try
        {
            var pedido = await _db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == pedidoId);
            if (pedido == null || pedido.ClienteId.HasValue) return;
            if (string.IsNullOrWhiteSpace(pedido.ClienteNome)) return;
            var nome = pedido.ClienteNome.Trim();
            if (string.Equals(nome, "Avulso", StringComparison.OrdinalIgnoreCase)) return;

            var candidatos = await _db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.EmpresaId == pedido.EmpresaId
                         && c.Nome.ToLower() == nome.ToLower())
                .Select(c => c.Id).Take(2).ToListAsync();
            if (candidatos.Count != 1) return;

            var clienteId = candidatos[0];
            var rows = await _db.Set<Pedido>().IgnoreQueryFilters()
                .Where(p => p.Id == pedidoId && p.ClienteId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ClienteId, clienteId));
            if (rows > 0)
                _log.LogInformation("F9-B: Pedido {PedidoId} linkado ao Cliente {ClienteId} por nome={Nome}",
                    pedidoId, clienteId, nome);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "F9-B EnsureClienteLink falhou pedido={PedidoId}: {Msg}", pedidoId, ex.Message);
        }
    }

    /// <summary>
    /// F8-J â€” aplica saÃ­da de estoque para pedido entregue. Idempotente via DocumentoReferencia.
    /// </summary>
    private async Task EnsureStockSaidaAsync(Order mobileO)
    {
        try
        {
            if (mobileO.EmpresaId == null) return;
            var jaAplicou = await _db.Set<MovimentacaoEstoque>()
                .IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(m => m.DocumentoReferencia == mobileO.Id
                            && m.Natureza == NaturezaMovimentacaoEstoque.Venda);
            if (jaAplicou) return;

            var trackedOrder = await _db.Set<Order>().IgnoreQueryFilters()
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == mobileO.Id);
            if (trackedOrder == null) return;

            foreach (var i in trackedOrder.Items)
            {
                if (string.IsNullOrWhiteSpace(i.ProductId)) continue;
                var p = await _db.Set<Product>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == i.ProductId && x.EmpresaId == mobileO.EmpresaId);
                if (p == null) continue;
                await _stockReconciler.ApplyDeltaAsync(
                    p, -i.Qty,
                    NaturezaMovimentacaoEstoque.Venda,
                    descricao: $"Pedido mobile {mobileO.Id} entregue (backfill F8-J)",
                    referenciaDocumento: mobileO.Id);
            }
            if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "F8-J StockSaida falhou pedido_mobile={MobileId}: {Msg}", mobileO.Id, ex.Message);
        }
    }

    /// <summary>
    /// F7-A/F8-C: garante 1 PedidoPagamento default + MovimentoCaixa de entrada
    /// para pedido entregue. Idempotente via AnyAsync check.
    /// </summary>
    private async Task EnsurePagamentoEntregueAsync(Guid pedidoId, Order mobileO)
    {
        var pedido = await _db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido == null) return;
        if (pedido.Total.Valor <= 0) return;

        var temPagamento = await _db.Set<PedidoPagamento>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(pp => pp.PedidoId == pedidoId);
        if (temPagamento) return;

        var pagamentoId = Guid.NewGuid();
        _db.Add(new PedidoPagamento
        {
            Id = pagamentoId,
            PedidoId = pedido.Id,
            Metodo = "dinheiro",
            Valor = pedido.Total.Valor,
            PagoEm = mobileO.UpdatedAt,
            RegistradoPorNome = mobileO.LastOperatorName,
            Observacao = "Auto-registrado pelo F7-A (mobileâ†’ERP). Refine mÃ©todo no admin se necessÃ¡rio."
        });

        var refKey = "pedido-pagamento:" + pagamentoId;
        var jaExisteMov = await _db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.Referencia == refKey);
        if (!jaExisteMov)
        {
            var mov = MovimentoCaixa.Criar(pedido.EmpresaId, "entrada", pedido.Total.Valor,
                dataMovimento: mobileO.UpdatedAt, lojaId: pedido.LojaId);
            mov.Descricao = "Pagamento pedido " + (pedido.Id.ToString().Substring(0, 8)) +
                            (string.IsNullOrEmpty(pedido.ClienteNome) ? "" : " â€” " + pedido.ClienteNome);
            mov.Metodo = "dinheiro";
            mov.Categoria = "pedido";
            mov.Origem = "mobile-payment";
            mov.Referencia = refKey;
            mov.RegistradoPorNome = mobileO.LastOperatorName;
            _db.Add(mov);
        }

        await _db.SaveChangesAsync();
        _log.LogInformation(
            "F7-A/F8-C Pagamento+MovCaixa CRIADO: pedido={ErpId} valor={Valor} metodo=dinheiro",
            pedido.Id, pedido.Total.Valor);
    }

    // F2 â€” promove mobile_batch â†’ Lote web. Idempotente via FindByMobileBatchIdAsync.
}

/// <summary>Counts returned by BackfillAsync for the HTTP response.</summary>
public record BackfillCounts(int Products, int Clients, int Orders, int Batches, int CashEntries);
