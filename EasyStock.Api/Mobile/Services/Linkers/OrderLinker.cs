using EasyStock.Api.Mobile.DTOs;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
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
/// <para>
/// #1543 — quem é dono do quê. Pedido que <b>nasceu no aparelho</b>: o aparelho manda; o status do
/// Pedido do ERP espelha o dele e o estoque sai pelo <see cref="MobileStockReconciler"/>. Pedido que
/// <b>nasceu no ERP</b> (console, site, atendimento) e o aparelho só avançou: a troca de status passa
/// pela esteira do ERP (<see cref="AtualizarStatusPedidoUseCase"/>: máquina de estados, estoque,
/// outbox e avisos). Antes o status desse pedido era gravado direto na tabela, por fora de tudo isso.
/// </para>
/// </summary>
public sealed class OrderLinker(
    EasyStockDbContext db,
    IPedidoRepository pedidoRepo,
    MobileStockReconciler stockReconciler,
    MobileSaleSyncService saleSync,
    CriarPedidoUseCase criarPedidoUseCase,
    ILogger<OrderLinker> log,
    AtualizarStatusPedidoUseCase? atualizarStatus = null)
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

                if (!pedidoIdResolvido.HasValue) continue;

                // O vinculo fica gravado antes do status: se a esteira recusar a troca, o que ela
                // deixou pela metade e descartado sem levar o vinculo junto.
                if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
                var nasceuNoErp = await NasceuNoErpAsync(pedidoIdResolvido.Value);

                // F8-F: o status do aparelho chega ao Pedido do ERP.
                if (nasceuNoErp) await LevarStatusAEsteiraAsync(pedidoIdResolvido.Value, mobileO);
                else await EnsureStatusSyncAsync(pedidoIdResolvido.Value, mobileO);
                await EnsureClienteLinkAsync(pedidoIdResolvido.Value, mobileO);

                // F7-A — Pagamento auto quando mobileO.Status == "entregue" (entregar no aparelho e receber).
                if (string.Equals(mobileO.Status, "entregue", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsurePagamentoEntregueAsync(pedidoIdResolvido.Value, mobileO);
                    await EnsureVendaAsync(mobileO);
                }
                // #1543: o estoque do pedido do ERP e da esteira dele. No pedido do aparelho, o produto pode
                // ter sido ligado ao ERP so agora (nasceu no mesmo envio); a baixa que nao pode sair no
                // envio sai aqui, em qualquer status que ja desconta.
                if (!nasceuNoErp && SyncMutationDispatcher.StatusDescontaEstoque((mobileO.Status ?? "").ToLowerInvariant()))
                    await EnsureStockSaidaAsync(mobileO);
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

    private static readonly HashSet<string> StatusQueOAparelhoManda = ["aguardando", "preparando", "pronto", "entregue", "cancelado"];

    private async Task<bool> NasceuNoErpAsync(Guid pedidoId)
    {
        var origem = await db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == pedidoId).Select(p => p.Origem).FirstOrDefaultAsync();
        return origem != SyncMutationDispatcher.OrigemDoPwa;
    }

    /// <summary>
    /// #1543 — o aparelho avançou (ou cancelou) um pedido que nasceu no ERP. A troca passa pelo caso de
    /// uso do ERP, com o nível de Operador: valida a transição, baixa ou devolve o estoque pelos itens do
    /// pedido, grava o evento e publica no outbox. Se a esteira recusa (transição inválida, cancelamento
    /// de pedido pago, que é do Gerente), o status do ERP fica como estava e a recusa vai para o log.
    /// </summary>
    private async Task LevarStatusAEsteiraAsync(Guid pedidoId, Order mobileO)
    {
        var alvo = (mobileO.Status ?? "").ToLowerInvariant();
        if (!StatusQueOAparelhoManda.Contains(alvo) || mobileO.EmpresaId is not { } empresaId) return;
        try
        {
            var atual = await db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Id == pedidoId).Select(p => p.Status).FirstOrDefaultAsync();
            if (atual is null || atual == alvo) return;
            if (atualizarStatus is null)
            {
                log.LogWarning("Pedido do ERP {ErpId}: o aparelho pediu {Status}, mas a esteira não está disponível neste contexto. Nada foi gravado.",
                    pedidoId, alvo);
                return;
            }

            await atualizarStatus.ExecuteAsync(new AtualizarStatusPedidoCommand(
                empresaId, pedidoId, alvo,
                UsuarioNome: mobileO.LastOperatorName,
                Origem: SyncMutationDispatcher.OrigemDoPwa,
                OcorridoEm: mobileO.UpdatedAt));
            log.LogInformation("Pedido do ERP {ErpId}: {De} → {Para} pela esteira, a pedido do aparelho {Device}.",
                pedidoId, atual, alvo, mobileO.LastDeviceId);
        }
        catch (Exception ex)
        {
            // O caso de uso pode ter mexido em entidades antes de recusar; nada disso vai ao banco.
            db.DescartarMudancasPendentes();
            log.LogWarning(ex, "Pedido do ERP {ErpId}: a esteira recusou {Status} vindo do aparelho: {Msg}",
                pedidoId, alvo, ex.Message);
        }
    }

    /// <summary>F8-F — espelha no Pedido do ERP o status do pedido que nasceu no aparelho. Idempotente.</summary>
    private async Task EnsureStatusSyncAsync(Guid pedidoId, Order mobileO)
    {
        var alvo = (mobileO.Status ?? "").ToLowerInvariant();
        if (!StatusQueOAparelhoManda.Contains(alvo)) return;
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

    /// <summary>
    /// F8-J — garante a saída de estoque do pedido do aparelho que já desconta. O envio do pedido já
    /// baixa (dispatcher); isto cobre o produto que só ganhou vínculo com o ERP depois. Não repete o
    /// que já saiu por este pedido, nem refaz saída que alguém estornou no ERP.
    /// </summary>
    private async Task EnsureStockSaidaAsync(Order mobileO)
    {
        try
        {
            if (mobileO.EmpresaId == null) return;
            var jaSaiu = await db.Set<MovimentacaoEstoque>()
                .IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.EmpresaId == mobileO.EmpresaId && m.DocumentoReferencia == mobileO.Id
                            && m.Natureza == NaturezaMovimentacaoEstoque.Venda)
                .Select(m => m.ProdutoId).Distinct().ToListAsync();

            foreach (var doProduto in mobileO.Items.Where(i => !string.IsNullOrWhiteSpace(i.ProductId)).GroupBy(i => i.ProductId))
            {
                var p = await db.Set<Product>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == doProduto.Key && x.EmpresaId == mobileO.EmpresaId);
                if (p?.ErpProductId is not { } produtoErp || jaSaiu.Contains(produtoErp)) continue;
                await stockReconciler.BaixarVendaAsync(p, doProduto.Sum(i => i.Qty), mobileO.Id,
                    $"Pedido mobile {mobileO.Id} -> {mobileO.Status}");
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "F8-J StockSaida falhou pedido_mobile={MobileId}: {Msg}", mobileO.Id, ex.Message);
        }
    }

    /// <summary>F7-A: garante 1 PedidoPagamento default para pedido entregue. Idempotente via
    /// AnyAsync check. NAO cria MovimentoCaixa "entrada" espelho: ele duplicava o pagamento no
    /// resumo do caixa e no dashboard (double-count #564 / QA BUG-004). O fluxo web/balcao nunca
    /// criou esse espelho — o mobile agora fica consistente.</summary>
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
        var metodo = FormaPagamentoMobile.ParaErp(mobileO.Metodo);
        db.Add(new PedidoPagamento
        {
            Id = pagamentoId,
            PedidoId = pedido.Id,
            Metodo = metodo,
            Valor = pedido.Total.Valor,
            PagoEm = mobileO.UpdatedAt,
            RegistradoPorNome = mobileO.LastOperatorName,
            Observacao = mobileO.Metodo is null
                ? "Auto-registrado pelo F7-A (mobile→ERP). Aparelho sem forma de pagamento: assumido dinheiro."
                : "Auto-registrado pelo F7-A (mobile→ERP) com a forma escolhida no PWA."
        });

        // BUG-004/#564: NAO criar MovimentoCaixa "entrada" espelho aqui. Ele duplicava o
        // PedidoPagamento acima — o resumo do caixa (totalPagamentosPedidos) e o card do dashboard
        // (pagamentosNoSaldo) ja somam esse pagamento, entao o espelho fazia o mesmo dinheiro contar
        // 2x. O fluxo web/balcao (RegistrarPagamentoPedidoUseCase) nunca criou o espelho. Espelhos
        // historicos sao limpos por SQL idempotente separado (movimentos_caixa Referencia LIKE
        // 'pedido-pagamento:%').

        await db.SaveChangesAsync();
        log.LogInformation(
            "F7-A Pagamento CRIADO: pedido={ErpId} valor={Valor} metodo={Metodo}",
            pedido.Id, pedido.Total.Valor, metodo);
    }
}
