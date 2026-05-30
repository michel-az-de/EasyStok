using EasyStock.Api.Mobile.DTOs;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services.Linkers;

/// <summary>
/// Auto-linker: promove <c>Order</c> mobile a <c>Pedido</c> ERP. Maior linker da F8:
/// orquestra criação do pedido via CriarPedidoUseCase, links de FKs (Cliente, Produto),
/// sync de status (F8-F), match de cliente por nome (F9-B), e quando entregue: cria
/// Pagamento + MovCaixa (F7-A/F8-C), Venda (F8-G), saída de estoque (F8-J).
///
/// Idempotente em múltiplas camadas: FindByMobileOrderIdAsync, Guid.TryParse do mobile.Id,
/// AnyAsync para Pagamento, DocumentoReferencia para estoque, mobileO.ErpVendaId para Venda.
///
/// Extraido do god-Service <c>SyncAutoLinker</c> (F8 final).
/// </summary>
public sealed class OrderLinker(
    EasyStockDbContext db,
    IPedidoRepository pedidoRepo,
    MobileStockReconciler stockReconciler,
    MobileSaleSyncService saleSync,
    CriarPedidoUseCase criarPedidoUseCase,
    ILogger<OrderLinker> log)
{
    public async Task ExecuteAsync(IEnumerable<string> mobileOrderIds, Guid? empresaId)
    {
        var idsList = mobileOrderIds as ICollection<string> ?? mobileOrderIds.ToList();
        if (!empresaId.HasValue)
        {
            log.LogWarning(
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
                var mobileO = await db.Set<Order>().IgnoreQueryFilters()
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
                    var jaPromovido = await pedidoRepo.FindByMobileOrderIdAsync(empresaId.Value, mobileO.Id);
                    if (jaPromovido != null)
                    {
                        mobileO.ErpPedidoId = jaPromovido.Id;
                        mobileO.UpdatedAt = DateTime.UtcNow;
                        pedidoIdResolvido = jaPromovido.Id;
                        log.LogInformation("AutoLink Pedido (idempotente MobileOrderId): mobile={MobileId} → erp={ErpId}", oid, jaPromovido.Id);
                    }
                    // F6 idempotencia: pull web→mobile retornou Pedido web com Guid,
                    // APK reenfileirou de volta com mobile.Id=Guid.
                    else if (Guid.TryParse(mobileO.Id, out var pedidoIdAsGuid))
                    {
                        var pedidoExistente = await pedidoRepo.GetByIdAsync(empresaId.Value, pedidoIdAsGuid);
                        if (pedidoExistente != null)
                        {
                            mobileO.ErpPedidoId = pedidoExistente.Id;
                            mobileO.UpdatedAt = DateTime.UtcNow;
                            if (string.IsNullOrEmpty(pedidoExistente.MobileOrderId))
                            {
                                pedidoExistente.MobileOrderId = mobileO.Id;
                                await pedidoRepo.UpdateAsync(pedidoExistente);
                            }
                            pedidoIdResolvido = pedidoExistente.Id;
                            log.LogInformation("AutoLink Pedido (idempotente Guid eco): mobile={MobileId} ↔ erp={ErpId}",
                                oid, pedidoExistente.Id);
                        }
                    }

                    if (pedidoIdResolvido == null)
                    {
                        Client? mClient = null;
                        if (!string.IsNullOrWhiteSpace(mobileO.ClientId))
                        {
                            mClient = await db.Set<Client>().IgnoreQueryFilters().AsNoTracking()
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
                        var result = await criarPedidoUseCase.ExecuteAsync(new CriarPedidoCommand(
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
                                var existeErp = await db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
                                    .AnyAsync(c => c.Id == mClient.ErpClienteId.Value && c.EmpresaId == empresaId);
                                if (existeErp) clienteFk = mClient.ErpClienteId;
                            }
                            if (clienteFk.HasValue)
                            {
                                await db.Set<Pedido>().IgnoreQueryFilters()
                                    .Where(p => p.Id == result.Id)
                                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ClienteId, clienteFk));
                            }

                            var productIds = mobileO.Items.Where(i => !string.IsNullOrWhiteSpace(i.ProductId))
                                .Select(i => i.ProductId).Distinct().ToList();
                            var produtoMap = await db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                                .Where(p => productIds.Contains(p.Id) && p.EmpresaId == empresaId)
                                .ToDictionaryAsync(p => p.Id, p => p.ErpProductId);

                            var pedidoItens = await db.Set<PedidoItem>().IgnoreQueryFilters()
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
                            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
                        }
                        catch (Exception linkEx)
                        {
                            log.LogWarning(linkEx, "Pedido {ErpId}: falha ao linkar Cliente/Produto FKs (pedido criado mas ad-hoc). {Msg}",
                                result.Id, linkEx.Message);
                        }

                        mobileO.ErpPedidoId = result.Id;
                        mobileO.UpdatedAt = DateTime.UtcNow;
                        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
                        pedidoIdResolvido = result.Id;
                        log.LogInformation("AutoLink Pedido CRIADO: mobile={MobileId} → erp={ErpId} status={Status}",
                            oid, result.Id, mobileO.Status);
                    }
                }

                // F8-F: sincroniza Status do Pedido com mobileO.Status SEMPRE.
                if (pedidoIdResolvido.HasValue)
                {
                    await EnsureStatusSyncAsync(pedidoIdResolvido.Value, mobileO);
                    await EnsureClienteLinkAsync(pedidoIdResolvido.Value, mobileO);
                }

                // F7-A — Pagamento auto quando mobileO.Status == "entregue".
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
                log.LogError(ex,
                    "AutoLink Pedido FALHOU mobile={MobileId} empresaId={EmpresaId} exType={ExType}: {Mensagem}",
                    oid, empresaId, ex.GetType().Name, ex.Message);
            }
        }
        log.LogInformation(
            "AutoLink Pedido summary empresaId={EmpresaId} total={Total} processed={Processed} errors={Errors}",
            empresaId, idsList.Count, processed, errorSkip);
    }

    /// <summary>F8-F — sincroniza Status do Pedido web com mobileO.Status. Idempotente.</summary>
    private async Task EnsureStatusSyncAsync(Guid pedidoId, Order mobileO)
    {
        var alvo = (mobileO.Status ?? "").ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(alvo)) return;
        var validos = new HashSet<string> { "aguardando", "preparando", "pronto", "entregue", "cancelado" };
        if (!validos.Contains(alvo)) return;
        try
        {
            var rows = await db.Set<Pedido>().IgnoreQueryFilters()
                .Where(p => p.Id == pedidoId && p.Status != alvo)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, alvo)
                    .SetProperty(p => p.AlteradoEm, DateTime.UtcNow)
                    .SetProperty(p => p.EntreguEm, p =>
                        alvo == "entregue" ? (DateTime?)mobileO.UpdatedAt : p.EntreguEm)
                    .SetProperty(p => p.CanceladoEm, p =>
                        alvo == "cancelado" ? (DateTime?)mobileO.UpdatedAt : p.CanceladoEm));
            if (rows > 0)
                log.LogInformation("F8-F Status sincronizado: pedido={ErpId} → {Status}", pedidoId, alvo);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "F8-F Status sync falhou pedido={ErpId}: {Msg}", pedidoId, ex.Message);
        }
    }

    /// <summary>F8-G — cria Venda quando pedido entregue. Idempotente via mobileO.ErpVendaId.</summary>
    private async Task EnsureVendaAsync(Order mobileO)
    {
        try
        {
            var trackedOrder = await db.Set<Order>().IgnoreQueryFilters()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == mobileO.Id);
            if (trackedOrder == null) return;

            if (!trackedOrder.ErpVendaId.HasValue)
            {
                var created = await saleSync.CreateVendaForDeliveredOrderAsync(trackedOrder, trackedOrder.Items.Select(i => new OrderItemDto(
                    ProductId: i.ProductId ?? "",
                    Name: i.Name,
                    Emoji: i.Emoji,
                    Unit: i.Unit,
                    Qty: i.Qty,
                    UnitPrice: i.UnitPrice
                )).ToList());
                if (created && db.ChangeTracker.HasChanges())
                    await db.SaveChangesAsync();
            }

            // F9-A: popula Pedido.VendaId retroativamente.
            if (trackedOrder.ErpVendaId.HasValue && trackedOrder.ErpPedidoId.HasValue)
            {
                await db.Set<Pedido>().IgnoreQueryFilters()
                    .Where(p => p.Id == trackedOrder.ErpPedidoId.Value && p.VendaId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.VendaId, trackedOrder.ErpVendaId.Value));
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "F8-G Venda falhou pedido_mobile={MobileId}: {Msg}", mobileO.Id, ex.Message);
        }
    }

    /// <summary>F9-B: re-linka Pedido.ClienteId via match por nome. Idempotente.</summary>
    private async Task EnsureClienteLinkAsync(Guid pedidoId, Order mobileO)
    {
        try
        {
            var pedido = await db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == pedidoId);
            if (pedido == null || pedido.ClienteId.HasValue) return;
            if (string.IsNullOrWhiteSpace(pedido.ClienteNome)) return;
            var nome = pedido.ClienteNome.Trim();
            if (string.Equals(nome, "Avulso", StringComparison.OrdinalIgnoreCase)) return;

            var candidatos = await db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.EmpresaId == pedido.EmpresaId
                         && c.Nome.ToLower() == nome.ToLower())
                .Select(c => c.Id).Take(2).ToListAsync();
            if (candidatos.Count != 1) return;

            var clienteId = candidatos[0];
            var rows = await db.Set<Pedido>().IgnoreQueryFilters()
                .Where(p => p.Id == pedidoId && p.ClienteId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ClienteId, clienteId));
            if (rows > 0)
                log.LogInformation("F9-B: Pedido {PedidoId} linkado ao Cliente {ClienteId} por nome={Nome}",
                    pedidoId, clienteId, nome);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "F9-B EnsureClienteLink falhou pedido={PedidoId}: {Msg}", pedidoId, ex.Message);
        }
    }

    /// <summary>F8-J — aplica saída de estoque para pedido entregue. Idempotente via DocumentoReferencia.</summary>
    private async Task EnsureStockSaidaAsync(Order mobileO)
    {
        try
        {
            if (mobileO.EmpresaId == null) return;
            var jaAplicou = await db.Set<MovimentacaoEstoque>()
                .IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(m => m.DocumentoReferencia == mobileO.Id
                            && m.Natureza == NaturezaMovimentacaoEstoque.Venda);
            if (jaAplicou) return;

            var trackedOrder = await db.Set<Order>().IgnoreQueryFilters()
                .Include(o => o.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == mobileO.Id);
            if (trackedOrder == null) return;

            foreach (var i in trackedOrder.Items)
            {
                if (string.IsNullOrWhiteSpace(i.ProductId)) continue;
                var p = await db.Set<Product>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == i.ProductId && x.EmpresaId == mobileO.EmpresaId);
                if (p == null) continue;
                await stockReconciler.ApplyDeltaAsync(
                    p, -i.Qty,
                    NaturezaMovimentacaoEstoque.Venda,
                    descricao: $"Pedido mobile {mobileO.Id} entregue (backfill F8-J)",
                    referenciaDocumento: mobileO.Id);
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "F8-J StockSaida falhou pedido_mobile={MobileId}: {Msg}", mobileO.Id, ex.Message);
        }
    }

    /// <summary>F7-A/F8-C: garante 1 PedidoPagamento default + MovimentoCaixa de entrada
    /// para pedido entregue. Idempotente via AnyAsync check.</summary>
    private async Task EnsurePagamentoEntregueAsync(Guid pedidoId, Order mobileO)
    {
        var pedido = await db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido == null) return;
        if (pedido.Total.Valor <= 0) return;

        var temPagamento = await db.Set<PedidoPagamento>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(pp => pp.PedidoId == pedidoId);
        if (temPagamento) return;

        var pagamentoId = Guid.NewGuid();
        db.Add(new PedidoPagamento
        {
            Id = pagamentoId,
            PedidoId = pedido.Id,
            Metodo = "dinheiro",
            Valor = pedido.Total.Valor,
            PagoEm = mobileO.UpdatedAt,
            RegistradoPorNome = mobileO.LastOperatorName,
            Observacao = "Auto-registrado pelo F7-A (mobile→ERP). Refine método no admin se necessário."
        });

        var refKey = "pedido-pagamento:" + pagamentoId;
        var jaExisteMov = await db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.Referencia == refKey);
        if (!jaExisteMov)
        {
            var mov = MovimentoCaixa.Criar(pedido.EmpresaId, "entrada", pedido.Total.Valor,
                dataMovimento: mobileO.UpdatedAt, lojaId: pedido.LojaId);
            mov.Descricao = "Pagamento pedido " + (pedido.Id.ToString().Substring(0, 8)) +
                            (string.IsNullOrEmpty(pedido.ClienteNome) ? "" : " — " + pedido.ClienteNome);
            mov.Metodo = "dinheiro";
            mov.Categoria = "pedido";
            mov.Origem = "mobile-payment";
            mov.Referencia = refKey;
            mov.RegistradoPorNome = mobileO.LastOperatorName;
            db.Add(mov);
        }

        await db.SaveChangesAsync();
        log.LogInformation(
            "F7-A/F8-C Pagamento+MovCaixa CRIADO: pedido={ErpId} valor={Valor} metodo=dinheiro",
            pedido.Id, pedido.Total.Valor);
    }
}
