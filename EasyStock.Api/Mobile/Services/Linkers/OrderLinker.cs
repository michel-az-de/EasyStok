using EasyStock.Api.Mobile.DTOs;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
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
                var nasceuNoErp = await SyncMutationDispatcher.PedidoNasceuNoErpAsync(db, empresaId, pedidoIdResolvido.Value);

                // F8-F: o status do aparelho chega ao Pedido do ERP.
                var erpAcompanhou = true;
                if (nasceuNoErp) erpAcompanhou = await LevarStatusAEsteiraAsync(pedidoIdResolvido.Value, mobileO);
                else await EnsureStatusSyncAsync(pedidoIdResolvido.Value, mobileO);
                await EnsureClienteLinkAsync(pedidoIdResolvido.Value, mobileO);

                // F7-A — Pagamento auto quando mobileO.Status == "entregue" (entregar no aparelho e receber).
                // Se a esteira recusou a entrega do pedido do ERP, ele não está entregue: não recebe.
                if (erpAcompanhou && string.Equals(mobileO.Status, "entregue", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsurePagamentoEntregueAsync(pedidoIdResolvido.Value, mobileO);
                    await EnsureVendaAsync(mobileO);
                }
                // A Venda do pedido do ERP é deste vínculo (o envio não mexe nela): só é marcada como
                // cancelada quando a esteira aceitou o cancelamento.
                else if (nasceuNoErp && erpAcompanhou && string.Equals(mobileO.Status, "cancelado", StringComparison.OrdinalIgnoreCase))
                    await CancelarVendaAsync(mobileO);
                // #1543: o estoque do pedido do ERP e da esteira dele. No pedido do aparelho, o produto pode
                // ter sido ligado ao ERP so agora (nasceu no mesmo envio); a baixa que nao pode sair no
                // envio sai aqui, em qualquer status que ja desconta.
                if (!nasceuNoErp && SyncMutationDispatcher.StatusDescontaEstoque((mobileO.Status ?? "").ToLowerInvariant()))
                    await EnsureStockSaidaAsync(mobileO);
            }
            catch (Exception ex)
            {
                errorSkip++;
                // O que este pedido deixou pela metade não pode ir ao banco junto com o próximo.
                db.DescartarMudancasPendentes();
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

    /// <summary>Início da observação do recebimento que este vínculo registra sozinho na entrega (F7-A).</summary>
    public const string MarcaDoRecebimentoAutomatico = "Auto-registrado pelo F7-A";

    /// <summary>
    /// #1543 — o aparelho avançou (ou cancelou) um pedido que nasceu no ERP. A troca passa pelo caso de
    /// uso do ERP: valida a transição, baixa ou devolve o estoque pelos itens do pedido, grava o evento
    /// e publica no outbox.
    /// <para>
    /// O aparelho junta na fila os toques do mesmo pedido e manda só o último status ("pronto" para um
    /// pedido que o ERP ainda tem em "aguardando"). A esteira não aceita salto, então o caminho é
    /// percorrido passo a passo (<see cref="PedidoStateMachine.CaminhoPelaCozinha"/>).
    /// </para>
    /// <para>
    /// Sem caminho (pedido aguardando pagamento ou aprovação, pedido já cancelado no ERP), o ERP fica
    /// como estava. Se a esteira recusa um passo, os passos já dados ficam (cada um é uma troca válida
    /// e gravada) e o pedido para ali. O motivo vai para o log, e o próximo envio do pedido, ou o
    /// backfill, retoma de onde parou.
    /// </para>
    /// </summary>
    /// <returns>Se o pedido do ERP terminou no status que o aparelho mandou.</returns>
    private async Task<bool> LevarStatusAEsteiraAsync(Guid pedidoId, Order mobileO)
    {
        var alvoTexto = (mobileO.Status ?? "").ToLowerInvariant();
        if (!StatusQueOAparelhoManda.Contains(alvoTexto) || mobileO.EmpresaId is not { } empresaId) return false;
        if (!StatusPedidoMapper.TryParse(alvoTexto, out var alvo)) return false;
        try
        {
            var atualTexto = await db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Id == pedidoId && p.EmpresaId == empresaId).Select(p => p.Status).FirstOrDefaultAsync();
            if (!StatusPedidoMapper.TryParse(atualTexto, out var atual)) return false;
            if (atual == alvo) return true;
            if (atualizarStatus is null)
            {
                log.LogWarning("Pedido do ERP {ErpId}: o aparelho pediu {Status}, mas a esteira não está disponível neste contexto. Nada foi gravado.",
                    pedidoId, alvoTexto);
                return false;
            }
            if (PedidoStateMachine.CaminhoPelaCozinha(atual, alvo) is not { } caminho)
            {
                log.LogWarning("Pedido do ERP {ErpId}: está em {De} e o aparelho {Device} pediu {Para}. Não há caminho na esteira; o ERP fica como está.",
                    pedidoId, atualTexto, mobileO.LastDeviceId, alvoTexto);
                return false;
            }

            var nivel = alvo == StatusPedido.Cancelado && await SoTemRecebimentoDoProprioVinculoAsync(pedidoId, empresaId)
                ? NivelAcesso.Gerente
                : NivelAcesso.Operador;
            for (var i = 0; i < caminho.Count; i++)
            {
                // Cada passo com o seu instante (o último é o do toque), para a trilha do pedido sair na ordem.
                var ocorridoEm = mobileO.UpdatedAt.AddMilliseconds(i - (caminho.Count - 1));
                var resultado = await atualizarStatus.ExecuteAsync(new AtualizarStatusPedidoCommand(
                    empresaId, pedidoId, StatusPedidoMapper.Format(caminho[i]),
                    UsuarioNome: mobileO.LastOperatorName,
                    Origem: SyncMutationDispatcher.OrigemDoPwa,
                    OcorridoEm: ocorridoEm,
                    NivelSolicitante: nivel));
                if (resultado is null)
                {
                    log.LogWarning("Pedido do ERP {ErpId}: a esteira não achou o pedido ao levar {Status}.", pedidoId, alvoTexto);
                    return false;
                }
            }
            log.LogInformation("Pedido do ERP {ErpId}: {De} → {Para} pela esteira, a pedido do aparelho {Device}.",
                pedidoId, atualTexto, alvoTexto, mobileO.LastDeviceId);
            return true;
        }
        catch (Exception ex)
        {
            // O caso de uso pode ter mexido em entidades antes de recusar; nada disso vai ao banco.
            db.DescartarMudancasPendentes();
            log.LogWarning(ex, "Pedido do ERP {ErpId}: a esteira recusou {Status} vindo do aparelho: {Msg}",
                pedidoId, alvoTexto, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// O aparelho cancela um pedido já entregue, e o único recebimento dele é o que este vínculo
    /// registrou sozinho no ato da entrega (<see cref="EnsurePagamentoEntregueAsync"/>). É o aparelho
    /// desfazendo a entrega que ele mesmo registrou: no ERP o cancelamento de pedido pago é do gerente,
    /// e sem este caso o pedido ficaria entregue e pago para sempre, com o aparelho dizendo cancelado.
    /// Recebimento lançado por alguém no ERP (tem usuário, ou não leva a marca do automático), ou
    /// cobrança paga no provedor, segue a regra do ERP.
    /// </summary>
    private async Task<bool> SoTemRecebimentoDoProprioVinculoAsync(Guid pedidoId, Guid empresaId)
    {
        var recebimentos = await db.Set<PedidoPagamento>().IgnoreQueryFilters().AsNoTracking()
            .Where(pp => pp.PedidoId == pedidoId)
            .Select(pp => new { pp.Observacao, pp.RegistradoPorUserId })
            .ToListAsync();
        if (recebimentos.Count == 0) return false;
        if (recebimentos.Any(r => r.RegistradoPorUserId is not null
                                  || r.Observacao?.StartsWith(MarcaDoRecebimentoAutomatico, StringComparison.Ordinal) != true))
            return false;
        return !await db.Set<CobrancaPedido>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(c => c.PedidoId == pedidoId && c.EmpresaId == empresaId && c.Status == StatusCobrancaPedido.Paga);
    }

    /// <summary>Marca como cancelada a Venda que a entrega pelo aparelho criou. Idempotente.</summary>
    private async Task CancelarVendaAsync(Order mobileO)
    {
        try
        {
            var rastreado = await db.Set<Order>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(o => o.Id == mobileO.Id && o.EmpresaId == mobileO.EmpresaId);
            if (rastreado is not null && await saleSync.CancelVendaForOrderAsync(rastreado) && db.ChangeTracker.HasChanges())
                await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            db.DescartarMudancasPendentes();
            log.LogWarning(ex, "Venda do pedido_mobile={MobileId} não foi marcada como cancelada: {Msg}", mobileO.Id, ex.Message);
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
            db.DescartarMudancasPendentes();
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

            var linhas = new List<(Product Produto, int Quantidade)>();
            foreach (var doProduto in mobileO.Items.Where(i => !string.IsNullOrWhiteSpace(i.ProductId)).GroupBy(i => i.ProductId))
            {
                var p = await db.Set<Product>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(x => x.Id == doProduto.Key && x.EmpresaId == mobileO.EmpresaId);
                if (p is not null) linhas.Add((p, doProduto.Sum(i => i.Qty)));
            }
            foreach (var (produto, quantidade) in MobileStockReconciler.PorProdutoDoErp(linhas))
            {
                if (jaSaiu.Contains(produto.ErpProductId!.Value)) continue;
                await stockReconciler.BaixarVendaAsync(produto, quantidade, mobileO.Id, mobileO.LojaId,
                    $"Pedido mobile {mobileO.Id} -> {mobileO.Status}");
            }
            if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // A baixa que não gravou sai do contexto; senão derruba o SaveChanges dos pedidos seguintes.
            db.DescartarMudancasPendentes();
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
                ? $"{MarcaDoRecebimentoAutomatico} (mobile→ERP). Aparelho sem forma de pagamento: assumido dinheiro."
                : $"{MarcaDoRecebimentoAutomatico} (mobile→ERP) com a forma escolhida no PWA."
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
