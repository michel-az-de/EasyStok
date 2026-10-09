using EasyStock.Application.Events.Estoque;
using EasyStock.Application.Ports.Output.Integration;
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
///   - Decrementa do primeiro lote (FIFO simples por validade), permitindo
///     saldo negativo (a regra contábil rígida está em `RegistrarSaidaEstoqueUseCase`;
///     aqui estamos no caminho rápido do pedido).
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
    ILogger<PedidoEstoqueIntegrationService> logger)
{
    private bool PermiteEstoqueNegativo => options.Value.PermiteEstoqueNegativo;
    private bool RequerEstoqueExistente => options.Value.RequerEstoqueExistente;

    public async Task DescontarAsync(PedidoEntity pedido, CancellationToken ct = default)
    {
        if (!pedido.LojaId.HasValue) { logger.LogDebug("Pedido {Id} sem LojaId — sem desconto de estoque.", pedido.Id); return; }
        var lojaId = pedido.LojaId.Value;

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

            // Lotes elegiveis da loja em FEFO. Bloqueado/Descartado nunca baixam; vencido segue
            // permitido (a cozinha ja produziu). Lotes com saldo vem primeiro: antes, um lote velho
            // zerado era sempre o escolhido, gerava descoberto falso e o lote com saldo nunca baixava.
            var itens = await itemEstoqueRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value);
            var candidatos = (itens ?? [])
                .Where(i => i.LojaId == lojaId
                            && i.Status != StatusItemEstoque.Bloqueado
                            && i.Status != StatusItemEstoque.Descartado)
                .OrderBy(i => i.ValidadeEm ?? DateTime.MaxValue)
                .ToList();
            if (candidatos.Count == 0)
            {
                if (RequerEstoqueExistente)
                    throw new UseCaseValidationException(
                        $"Item '{item.Nome}': produto {item.ProdutoId} não tem estoque cadastrado na loja {lojaId}.");

                logger.LogWarning("Pedido {Id}: produto {ProdId} sem ItemEstoque operavel na loja {LojaId} — ignorando desconto.",
                    pedido.Id, item.ProdutoId, lojaId);
                continue;
            }

            // Quantidade agora é decimal — suporta frações (kg, litros, etc.).
            // Cap superior: 99.999 evita valores absurdos.
            var qtd = item.Quantidade;
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

            var restante = qtd;
            var falta = 0m;
            var tocados = new List<(ItemEstoque Lote, decimal Quantidade)>();
            ItemEstoque? ultimo = null;

            foreach (var candidato in candidatos.Where(c => (c.QuantidadeAtual?.Value ?? 0m) > 0m))
            {
                if (restante <= 0m) break;

                // Re-lê com FOR UPDATE para serializar atualizações concorrentes no mesmo lote.
                var lote = await itemEstoqueRepo.GetByIdComLockAsync(pedido.EmpresaId, candidato.Id) ?? candidato;
                var atualLote = lote.QuantidadeAtual?.Value ?? 0m;
                if (atualLote <= 0m) continue;

                var consumir = Math.Min(restante, atualLote);
                lote.RegistrarSaida(EasyStock.Domain.ValueObjects.Quantidade.From(consumir), agora, agora, permitirVencido: true);
                tocados.Add((lote, consumir));
                restante -= consumir;
                ultimo = lote;
            }

            if (restante > 0m)
            {
                // O saldo pode diminuir entre a consulta inicial e a aquisição dos locks.
                if (!PermiteEstoqueNegativo)
                    throw new EstoqueInsuficienteException(item.ProdutoId.Value, qtd, qtd - restante);

                // Falta vira descoberto auditavel no ultimo lote tocado (ou no 1o lote operavel se todos zerados).
                var alvoDescoberto = ultimo
                    ?? await itemEstoqueRepo.GetByIdComLockAsync(pedido.EmpresaId, candidatos[0].Id)
                    ?? candidatos[0];
                falta = restante;
                logger.LogWarning(
                    "Pedido {Id}: produto {ProdId} estoque insuficiente (atual={Atual}, pedido={Qty}) — {Falta} un a descoberto.",
                    pedido.Id, item.ProdutoId, disponivelTotal, qtd, falta);
                alvoDescoberto.RegistrarSaidaPermitindoDescoberto(EasyStock.Domain.ValueObjects.Quantidade.From(restante), agora, agora, permitirVencido: true);
                var idx = tocados.FindIndex(t => t.Lote.Id == alvoDescoberto.Id);
                if (idx >= 0) tocados[idx] = (tocados[idx].Lote, tocados[idx].Quantidade + restante);
                else tocados.Add((alvoDescoberto, restante));
                ultimo = alvoDescoberto;
            }

            // Atualiza velocidade de saída (média 30 dias) para manter rotatividade
            // correta no estoque — o caminho RegistrarSaidaEstoqueUseCase faz o mesmo.
            const int janelaDias = 30;
            var taxaAnterior = await movRepo.GetTaxaSaidaDiariaAsync(
                pedido.EmpresaId, item.ProdutoId.Value,
                agora.AddDays(-janelaDias), agora);
            var velocidadeAtualizada = (taxaAnterior * janelaDias + qtd) / janelaDias;

            foreach (var (lote, quantidadeLote) in tocados)
            {
                lote.AtualizarVelocidadeSaida(velocidadeAtualizada, agora);
                await itemEstoqueRepo.UpdateAsync(lote);

                var temFalta = falta > 0m && lote.Id == ultimo!.Id;
                await movRepo.InsertAsync(new MovimentacaoEstoque
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = pedido.EmpresaId,
                    ProdutoId = item.ProdutoId.Value,
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

    public async Task DevolverAsync(PedidoEntity pedido, CancellationToken ct = default)
    {
        if (!pedido.LojaId.HasValue) return;

        foreach (var item in pedido.Itens)
            await DevolverItemAsync(pedido, item, ct);
    }

    /// <summary>
    /// Estorna o estoque de UM item do pedido. Usado ao remover um item de um pedido que
    /// já teve o estoque descontado (Pronto/Entregue) — #939 — e como bloco do
    /// <see cref="DevolverAsync"/>. Idempotente: só estorna se houve saída (Venda) anterior
    /// por este pedido+item e ainda não há Estorno.
    /// </summary>
    public async Task DevolverItemAsync(PedidoEntity pedido, PedidoItem item, CancellationToken ct = default)
    {
        if (!pedido.LojaId.HasValue) return;
        var lojaId = pedido.LojaId.Value;

        if (!item.ProdutoId.HasValue || item.Quantidade <= 0) return;

        // Mesma chave de idempotência do desconto (refDocItem).
        var refDocItem = $"{pedido.Id}:{item.Id}";

        // Só devolve se houve saída anterior por este pedido+item.
        if (!await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDocItem, NaturezaMovimentacaoEstoque.Venda, ct))
            return;

        // Idempotência do estorno: se já há um Estorno referenciando este pedido+item, pula.
        if (await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDocItem, NaturezaMovimentacaoEstoque.Estorno, ct))
            return;

        // Devolve para os lotes que de fato baixaram (um movimento de saida por lote), nao para
        // "o primeiro lote": a parte que virou descoberto e abatida antes de voltar ao saldo.
        var saidas = (await movRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value))
            .Where(m => m.DocumentoReferencia == refDocItem
                        && m.Tipo == TipoMovimentacaoEstoque.Saida
                        && m.Natureza == NaturezaMovimentacaoEstoque.Venda)
            .ToList();
        var qtd = item.Quantidade;
        if (qtd <= 0m) return;

        var agora = DateTime.UtcNow;
        if (saidas.Count == 0)
        {
            // Saida registrada mas movimento nao localizavel (dado legado): devolve ao primeiro lote da loja.
            var legado = (await itemEstoqueRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value))
                ?.Where(i => i.LojaId == lojaId)
                .OrderBy(i => i.ValidadeEm ?? DateTime.MaxValue)
                .FirstOrDefault();
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

            lote.RestaurarSaidaEstornada(saida.Quantidade, agora);
            await itemEstoqueRepo.UpdateAsync(lote);

            await movRepo.InsertAsync(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = pedido.EmpresaId,
                ProdutoId = item.ProdutoId.Value,
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

        const int janelaDias = 30;
        var taxaAnterior = await movRepo.GetTaxaSaidaDiariaAsync(
            pedido.EmpresaId, item.ProdutoId.Value,
            agora.AddDays(-janelaDias), agora);
        var velocidadeAtualizada = Math.Max(0m, (taxaAnterior * janelaDias - qtd) / janelaDias);
        foreach (var l in lotesDevolvidos.Values)
        {
            l.AtualizarVelocidadeSaida(velocidadeAtualizada, agora);
            await itemEstoqueRepo.UpdateAsync(l);
        }
    }
}
