using EasyStock.Application.Events.Estoque;
using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Domain.Services;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.Services;

/// <summary>
/// Opções de comportamento da integração Pedido↔Estoque. Mapeadas no API layer
/// via Configure&lt;PedidoEstoqueOptions&gt;(configuration.GetSection("Pedidos")).
/// </summary>
public sealed class PedidoEstoqueOptions
{
    /// <summary>
    /// Default true (S17 / RN-48): falta de saldo não trava o pedido — o saldo vai a zero,
    /// a falta vira <c>QuantidadeDescoberta</c> no lote e sai <c>estoque.desacerto</c>.
    /// false (rollback): throw EstoqueInsuficienteException; status update é abortado.
    /// </summary>
    public bool PermiteEstoqueNegativo { get; set; } = true;

    /// <summary>
    /// Quando true, exige ItemEstoque para todo item com ProdutoId. Default false:
    /// itens sem ItemEstoque são logados como warning e ignorados (graceful pra demo).
    /// </summary>
    public bool RequerEstoqueExistente { get; set; }
}

/// <summary>
/// Integração Pedido → Estoque. Quando um pedido transita para "entregue"
/// (ou "pronto"), descontamos os itens do estoque correspondente. Quando
/// transita "entregue/pronto" → "cancelado", devolvemos.
///
/// Comportamento defensivo:
///   - Se o item do pedido não tem ProdutoId, ignora (item ad-hoc).
///   - Se não há `ItemEstoque` ativo do produto na loja do pedido, ignora
///     com log de aviso (não quebra o status update).
///   - Decrementa os lotes pela regra única de consumo (`ConsumoDeLotes`: FEFO, vencido por
///     último, desempate pelo lote que entrou antes, `FifoAtivo` da loja), permitindo saldo
///     negativo (a regra contábil rígida está em `RegistrarSaidaEstoqueUseCase`; aqui estamos
///     no caminho rápido do pedido).
///   - Cria `MovimentacaoEstoque` rastreável (referência = PedidoId, descrição
///     menciona origem = pedido).
///
/// Idempotência:
///   - Antes de descontar, verifica se já existe movimentação com
///     `ReferenciaDocumento = pedidoId` e `Natureza = Venda` para o item.
///     Se sim, pula (status update sendo aplicado 2x não deduz 2x).
/// </summary>
public sealed class PedidoEstoqueIntegrationService(
    IItemEstoqueRepository itemEstoqueRepo,
    IMovimentacaoEstoqueRepository movRepo,
    IPublicadorEventoIntegracao publicadorEventos,
    Microsoft.Extensions.Options.IOptions<PedidoEstoqueOptions> options,
    ILogger<PedidoEstoqueIntegrationService> logger,
    IConfiguracaoLojaRepository? configuracaoLoja = null)
{
    private bool PermiteEstoqueNegativo => options.Value.PermiteEstoqueNegativo;
    private bool RequerEstoqueExistente => options.Value.RequerEstoqueExistente;

    /// <summary>
    /// Motivo gravado no estorno da saída que o cancelamento do pedido devolveu (#1571). Separa a
    /// devolução por cancelamento, que se refaz se o pedido voltar a valer, do estorno feito à mão.
    /// </summary>
    /// <remarks>
    /// O texto leva o prefixo para não coincidir com o motivo que alguém digita no estorno manual
    /// ("Pedido cancelado" é um motivo plausível lá).
    /// </remarks>
    public const string MotivoDoEstornoPorCancelamento = "Devolução automática: pedido cancelado";

    public Task DescontarAsync(PedidoEntity pedido, CancellationToken ct = default) =>
        DescontarAsync(pedido, itemNovo: null, ct);

    /// <param name="itemNovo">
    /// O item que acabou de entrar num pedido que já tinha baixado (#939). O aparelho não sabe dele,
    /// então a baixa do aparelho não o cobre (#1571): ele sai inteiro.
    /// </param>
    public async Task DescontarAsync(PedidoEntity pedido, PedidoItem? itemNovo, CancellationToken ct = default)
    {
        // #1534: o pedido do site e da comanda nasce sem loja (tenant único, uma cozinha) e baixa do
        // estoque da empresa; antes saía daqui sem descontar nada. Pedido com loja segue na loja dele.
        var lojaId = pedido.LojaId;
        var baixadoPeloAparelho = await BaixadoPeloAparelhoAsync(pedido, ct);
        var regra = await RegraDeConsumoAsync(pedido, ct);

        foreach (var item in pedido.Itens)
        {
            if (!item.ProdutoId.HasValue || item.Quantidade <= 0) continue;

            // Idempotência por ITEM (não por pedido): inclui PedidoItem.Id no
            // DocumentoReferencia para que pedidos com 2 itens do mesmo produto
            // não cancelem o segundo desconto pensando ser duplicação.
            var refDocItem = $"{pedido.Id}:{item.Id}";

            if (await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDocItem, NaturezaMovimentacaoEstoque.Venda, ct))
            {
                logger.LogDebug("Pedido {Id} item {ItemId}: movimentação já registrada (idempotência).", pedido.Id, item.Id);
                continue;
            }

            // #1571: o que o aparelho já baixou deste produto por este pedido não sai de novo; sai só a
            // diferença. O item que acabou de entrar fica fora dessa conta: o aparelho não o conhece.
            var qtd = item.Quantidade;
            if (item.Id != itemNovo?.Id
                && baixadoPeloAparelho.GetValueOrDefault(item.ProdutoId.Value) is var cobertura and > 0m)
            {
                var abatido = Math.Min(cobertura, qtd);
                baixadoPeloAparelho[item.ProdutoId.Value] = cobertura - abatido;
                qtd -= abatido;
                if (qtd <= 0m) continue;
            }

            // Lotes elegiveis da loja. Bloqueado/Descartado nunca baixam; vencido segue permitido (a cozinha
            // ja produziu), mas so depois dos que ainda valem. A ordem e a de ConsumoDeLotes, a mesma da venda
            // e do PWA (#2003): lote zerado nao baixa, FEFO, empate pelo que entrou antes.
            var itens = await itemEstoqueRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value);
            var candidatos = (itens ?? [])
                .Where(i => DaLojaDoPedido(i, lojaId)
                            && i.Status != StatusItemEstoque.Bloqueado
                            && i.Status != StatusItemEstoque.Descartado)
                .ToList();
            // M1.4c (#1537, D-M1-03): a linha de porção baixa do saldo da porção. Sem nenhum lote dela
            // (estoque antigo, lote do PWA), cai nos lotes sem porção do prato; nunca na porção errada.
            if (item.ProdutoVariacaoId is { } variacaoId)
            {
                var daPorcao = candidatos.Where(i => i.ProdutoVariacaoId == variacaoId).ToList();
                candidatos = daPorcao.Count > 0 ? daPorcao : candidatos.Where(i => i.ProdutoVariacaoId is null).ToList();
            }
            if (candidatos.Count == 0)
            {
                if (RequerEstoqueExistente)
                    throw new UseCaseValidationException(
                        $"Item '{item.Nome}': produto {item.ProdutoId} não tem estoque cadastrado {(lojaId is null ? "na empresa" : $"na loja {lojaId}")}.");

                logger.LogWarning("Pedido {Id}: produto {ProdId} sem ItemEstoque operavel na loja {LojaId} — ignorando desconto.",
                    pedido.Id, item.ProdutoId, lojaId);
                continue;
            }

            // Quantidade agora é decimal — suporta frações (kg, litros, etc.).
            // Cap superior: 99.999 evita valores absurdos.
            if (qtd > 99_999m)
                throw new UseCaseValidationException(
                    $"Item '{item.Nome}': quantidade {qtd} excede o teto de 99.999 unidades.");

            if (qtd <= 0m) continue;

            var agora = DateTime.UtcNow;
            var disponivelTotal = candidatos.Sum(c => c.QuantidadeAtual?.Value ?? 0m);

            // Estoque insuficiente (S17 / RN-48): por padrão avisa e não trava — saldo vai
            // a 0 e a falta vira QuantidadeDescoberta (#540). Rollback: PermiteEstoqueNegativo=false lança.
            if (disponivelTotal < qtd && !PermiteEstoqueNegativo)
                throw new EstoqueInsuficienteException(item.ProdutoId.Value, qtd, disponivelTotal);

            // Trava os lotes (FOR UPDATE) antes de planejar: o saldo pode ter mudado desde a consulta.
            var travados = new List<ItemEstoque>(candidatos.Count);
            foreach (var candidato in candidatos)
                travados.Add(await itemEstoqueRepo.GetByIdComLockAsync(pedido.EmpresaId, candidato.Id) ?? candidato);

            var plano = ConsumoDeLotes.Planejar(travados, qtd, agora, regra);
            var falta = plano.Falta;
            if (falta > 0m)
            {
                // O saldo pode diminuir entre a consulta inicial e a aquisição dos locks.
                if (!PermiteEstoqueNegativo)
                    throw new EstoqueInsuficienteException(item.ProdutoId.Value, qtd, qtd - falta);

                // Falta vira descoberto auditavel no ultimo lote tocado (ou no 1o lote da ordem se todos zerados).
                logger.LogWarning(
                    "Pedido {Id}: produto {ProdId} estoque insuficiente (atual={Atual}, pedido={Qty}) — {Falta} un a descoberto.",
                    pedido.Id, item.ProdutoId, disponivelTotal, qtd, falta);
            }
            plano.Aplicar(agora, agora, permitirVencido: true);
            var ultimo = plano.Consumos.Count > 0 ? plano.Consumos[^1].Lote : null;

            // Atualiza velocidade de saída (média 30 dias) para manter rotatividade
            // correta no estoque — o caminho RegistrarSaidaEstoqueUseCase faz o mesmo.
            var taxaAnterior = await movRepo.GetTaxaSaidaDiariaAsync(
                pedido.EmpresaId, item.ProdutoId.Value,
                agora.AddDays(-ItemEstoque.JanelaVelocidadeDias), agora);
            var velocidadeAtualizada = ItemEstoque.VelocidadeAposSaida(taxaAnterior, qtd);

            foreach (var (lote, quantidadeLote, descoberto) in plano.Consumos)
            {
                lote.AtualizarVelocidadeSaida(velocidadeAtualizada, agora);
                await itemEstoqueRepo.UpdateAsync(lote);

                var temFalta = descoberto > 0m;
                await movRepo.InsertAsync(new MovimentacaoEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = pedido.EmpresaId,
                    ProdutoId = item.ProdutoId.Value,
                    ProdutoVariacaoId = lote.ProdutoVariacaoId,
                    ItemEstoqueId = lote.Id,
                    Tipo = TipoMovimentacaoEstoque.Saida,
                    Natureza = NaturezaMovimentacaoEstoque.Venda,
                    Quantidade = EasyStock.Domain.ValueObjects.Quantidade.From(quantidadeLote),
                    ValorUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario),
                    ValorTotal = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario * quantidadeLote),
                    DocumentoReferencia = refDocItem,
                    DataMovimentacao = agora,
                    Descricao = temFalta
                        ? $"pedido {pedido.Id}: {falta} un a descoberto"
                        : $"Pedido {pedido.Id} item {item.Id}",
                    CriadoEm = agora
                });
            }

            // Mesma transação do caller (outbox antes do CommitAsync, ADR-0030).
            if (falta > 0m)
                await publicadorEventos.PublicarAsync(
                    pedido.EmpresaId,
                    EstoqueDesacertadoEvent.TipoEventoOutbox,
                    "Pedido",
                    pedido.Id,
                    new EstoqueDesacertadoEvent(
                        pedido.EmpresaId, pedido.LojaId, item.ProdutoId.Value, ultimo!.Id,
                        pedido.Id, falta, agora),
                    ct: ct);
        }
    }

    // FifoAtivo da loja do pedido (ou da loja única da empresa, pedido sem loja, #1534). Sem configuração, FEFO.
    private async Task<RegraDeConsumo> RegraDeConsumoAsync(PedidoEntity pedido, CancellationToken ct)
    {
        var configuracao = configuracaoLoja is null
            ? null
            : await configuracaoLoja.GetDaLojaAsync(pedido.EmpresaId, pedido.LojaId, ct);
        return new RegraDeConsumo(Fefo: configuracao?.FifoAtivo ?? true);
    }

    // Pedido sem loja enxerga todos os lotes da empresa; com loja, só os dela.
    private static bool DaLojaDoPedido(ItemEstoque lote, Guid? lojaDoPedido) =>
        lojaDoPedido is null || lote.LojaId == lojaDoPedido;

    public async Task DevolverAsync(PedidoEntity pedido, CancellationToken ct = default)
    {
        foreach (var item in pedido.Itens)
            await DevolverItemAsync(pedido, item, ct);
        await DevolverBaixaDoAparelhoAsync(pedido, ct);
    }

    /// <summary>
    /// #1571 — o pedido que nasceu no PWA baixa também pelo aparelho, com o id do pedido de lá no
    /// documento (<c>MobileStockReconciler</c>), e não com <c>{pedido}:{item}</c>. São duas chaves para
    /// a mesma venda: sem olhar a do outro lado, o console baixava de novo o que o aparelho já tinha
    /// baixado, e o cancelamento pelo console não devolvia a baixa do aparelho.
    /// </summary>
    /// <returns>Quanto o aparelho já baixou, por produto. Vazio no pedido que não veio do aparelho.</returns>
    private async Task<Dictionary<Guid, decimal>> BaixadoPeloAparelhoAsync(PedidoEntity pedido, CancellationToken ct) =>
        (await SaidasAbertasDoAparelhoAsync(pedido, ct))
            .GroupBy(s => s.ProdutoId)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantidade.Value));

    private async Task<IReadOnlyList<MovimentacaoEstoque>> SaidasAbertasDoAparelhoAsync(PedidoEntity pedido, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(pedido.MobileOrderId)
            ? []
            : await movRepo.GetSaidasAbertasPorDocumentoAsync(
                pedido.EmpresaId, pedido.MobileOrderId, NaturezaMovimentacaoEstoque.Venda, ct);

    /// <summary>
    /// O cancelamento pelo ERP devolve também o que o aparelho baixou por este pedido, aos lotes de
    /// onde saiu. Cada saída devolvida fica marcada, então o cancelamento do aparelho, se vier depois,
    /// não devolve de novo.
    /// </summary>
    private async Task DevolverBaixaDoAparelhoAsync(PedidoEntity pedido, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        foreach (var lida in await SaidasAbertasDoAparelhoAsync(pedido, ct))
        {
            var lote = await itemEstoqueRepo.GetByIdComLockAsync(pedido.EmpresaId, lida.ItemEstoqueId);
            // Relê a saída com trava: o aparelho pode ter cancelado (e devolvido) entre a leitura e aqui.
            var saida = await movRepo.GetByIdComLockAsync(pedido.EmpresaId, lida.Id, ct);
            if (saida is null || saida.EstornadaEm is not null) continue;

            var estorno = DevolucaoDeSaida.Aplicar(
                saida, lote, agora, $"Cancelamento pedido {pedido.Id}", MotivoDoEstornoPorCancelamento);
            if (lote is not null) await itemEstoqueRepo.UpdateAsync(lote);
            await movRepo.UpdateAsync(saida);
            await movRepo.InsertAsync(estorno);
        }
    }

    /// <summary>
    /// Estorna o estoque de UM item do pedido. Usado ao remover um item de um pedido que
    /// já teve o estoque descontado (Pronto/Entregue) — #939 — e como bloco do
    /// <see cref="DevolverAsync"/>. Idempotente: só estorna se houve saída (Venda) anterior
    /// por este pedido+item e ainda não há Estorno.
    /// </summary>
    public async Task DevolverItemAsync(PedidoEntity pedido, PedidoItem item, CancellationToken ct = default)
    {
        var lojaId = pedido.LojaId; // #1534: sem loja, o estoque é o da empresa.

        if (!item.ProdutoId.HasValue || item.Quantidade <= 0) return;

        // Mesma chave de idempotência do desconto (refDocItem).
        var refDocItem = $"{pedido.Id}:{item.Id}";

        // Só devolve se houve saída anterior por este pedido+item.
        if (!await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDocItem, NaturezaMovimentacaoEstoque.Venda, ct))
            return;

        // Devolve para os lotes que de fato baixaram (um movimento de saida por lote), nao para
        // "o primeiro lote": a parte que virou descoberto e abatida antes de voltar ao saldo.
        // #1564: o banco devolve só as deste pedido+item, e não o histórico inteiro do produto.
        var todasAsSaidas = (await movRepo.GetPorReferenciaAsync(
                pedido.EmpresaId, item.ProdutoId.Value, refDocItem, NaturezaMovimentacaoEstoque.Venda, ct))
            .Where(m => m.DocumentoReferencia == refDocItem
                        && m.Tipo == TipoMovimentacaoEstoque.Saida
                        && m.Natureza == NaturezaMovimentacaoEstoque.Venda)
            .ToList();

        // Idempotencia do estorno (#1506): cada saida devolvida fica marcada (EstornadaEm), entao so
        // volta o que ainda nao voltou. Antes, um estorno manual de UMA saida (mesmo
        // DocumentoReferencia) fazia o cancelamento pular as demais e o estoque ficava a menos.
        // Estorno sem nenhuma saida marcada e o legado anterior a esta regra: ja devolveu tudo.
        if (todasAsSaidas.All(s => s.EstornadaEm is null)
            && await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDocItem, NaturezaMovimentacaoEstoque.Estorno, ct))
            return;
        var saidas = todasAsSaidas.Where(s => s.EstornadaEm is null).ToList();
        if (todasAsSaidas.Count > 0 && saidas.Count == 0) return;

        var qtd = item.Quantidade;
        if (qtd <= 0m) return;

        var agora = DateTime.UtcNow;
        if (saidas.Count == 0)
        {
            // Saida registrada mas movimento nao localizavel (dado legado): devolve ao primeiro lote da loja.
            var daLoja = (await itemEstoqueRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value))
                ?.Where(i => DaLojaDoPedido(i, lojaId));
            var legado = daLoja is null ? null : ConsumoDeLotes.Ordenar(daLoja, agora, new RegraDeConsumo()).FirstOrDefault();
            if (legado is null) return;
            saidas.Add(new MovimentacaoEstoque
            {
                ItemEstoqueId = legado.Id,
                ItemEstoque = legado,
                Quantidade = EasyStock.Domain.ValueObjects.Quantidade.From(qtd)
            });
        }

        var lotesDevolvidos = new Dictionary<Guid, ItemEstoque>();
        foreach (var saida in saidas)
        {
            var lote = await itemEstoqueRepo.GetByIdComLockAsync(pedido.EmpresaId, saida.ItemEstoqueId) ?? saida.ItemEstoque;
            if (lote is null) continue;
            lotesDevolvidos[lote.Id] = lote;

            if (saida.Id != Guid.Empty)
            {
                var estorno = DevolucaoDeSaida.Aplicar(saida, lote, agora, $"Cancelamento pedido {pedido.Id} item {item.Id}");
                await itemEstoqueRepo.UpdateAsync(lote);
                await movRepo.UpdateAsync(saida);
                await movRepo.InsertAsync(estorno);
                continue;
            }

            // Dado legado: a saida nao tem movimento para marcar, so o saldo volta e o estorno nasce avulso.
            lote.RestaurarSaidaEstornada(saida.Quantidade, agora);
            await itemEstoqueRepo.UpdateAsync(lote);
            await movRepo.InsertAsync(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = pedido.EmpresaId,
                ProdutoId = item.ProdutoId.Value,
                ProdutoVariacaoId = lote.ProdutoVariacaoId,
                ItemEstoqueId = lote.Id,
                Tipo = TipoMovimentacaoEstoque.Entrada,
                Natureza = NaturezaMovimentacaoEstoque.Estorno,
                Quantidade = saida.Quantidade,
                ValorUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario),
                ValorTotal = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario * saida.Quantidade.Value),
                DocumentoReferencia = refDocItem,
                DataMovimentacao = agora,
                Descricao = $"Cancelamento pedido {pedido.Id} item {item.Id}",
                CriadoEm = agora
            });
        }

        var taxaAnterior = await movRepo.GetTaxaSaidaDiariaAsync(
            pedido.EmpresaId, item.ProdutoId.Value,
            agora.AddDays(-ItemEstoque.JanelaVelocidadeDias), agora);
        var velocidadeAtualizada = ItemEstoque.VelocidadeAposDevolucao(taxaAnterior, qtd);
        foreach (var l in lotesDevolvidos.Values)
        {
            l.AtualizarVelocidadeSaida(velocidadeAtualizada, agora);
            await itemEstoqueRepo.UpdateAsync(l);
        }
    }
}
