using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.Services;

/// <summary>
/// Opções de comportamento da integração Pedido↔Estoque. Mapeadas no API layer
/// via Configure&lt;PedidoEstoqueOptions&gt;(configuration.GetSection("Pedidos")).
/// </summary>
public sealed class PedidoEstoqueOptions
{
    /// <summary>
    /// Quando true, descontar pode resultar em saldo zero (clamp). Default false:
    /// throw EstoqueInsuficienteException; status update é abortado.
    /// </summary>
    public bool PermiteEstoqueNegativo { get; set; }

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

            var itens = await itemEstoqueRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value);
            var alvoCandidate = itens
                ?.Where(i => i.LojaId == lojaId)
                .OrderBy(i => i.ValidadeEm ?? DateTime.MaxValue)
                .FirstOrDefault();
            if (alvoCandidate is null)
            {
                if (RequerEstoqueExistente)
                    throw new UseCaseValidationException(
                        $"Item '{item.Nome}': produto {item.ProdutoId} não tem estoque cadastrado na loja {lojaId}.");

                logger.LogWarning("Pedido {Id}: produto {ProdId} sem ItemEstoque na loja {LojaId} — ignorando desconto.",
                    pedido.Id, item.ProdutoId, lojaId);
                continue;
            }

            // Re-lê com FOR UPDATE para serializar atualizações concorrentes
            // no mesmo item. Sem isso dois pedidos simultâneos podem ambos ler
            // a mesma quantidade e gerar saldo negativo.
            var alvo = await itemEstoqueRepo.GetByIdComLockAsync(pedido.EmpresaId, alvoCandidate.Id) ?? alvoCandidate;

            // Quantidade agora é decimal — suporta frações (kg, litros, etc.).
            // Cap superior: 99.999 evita valores absurdos.
            var qtd = item.Quantidade;
            if (qtd > 99_999m)
                throw new UseCaseValidationException(
                    $"Item '{item.Nome}': quantidade {qtd} excede o teto de 99.999 unidades.");

            if (qtd <= 0m) continue;

            var atual = alvo.QuantidadeAtual?.Value ?? 0m;

            // Estoque insuficiente: throw por padrão (status não muda),
            // ou clamp se PermiteEstoqueNegativo=true.
            if (atual < qtd)
            {
                if (!PermiteEstoqueNegativo)
                    throw new EstoqueInsuficienteException(
                        item.ProdutoId.Value, qtd, atual);

                logger.LogWarning(
                    "Pedido {Id}: produto {ProdId} estoque insuficiente (atual={Atual}, pedido={Qty}) — descontando só {AtualDescontado} (PermiteEstoqueNegativo=true).",
                    pedido.Id, item.ProdutoId, atual, qtd, atual);
                qtd = atual; // só desconta o que tem
            }

            alvo.QuantidadeAtual = EasyStock.Domain.ValueObjects.Quantidade.From(atual - qtd);

            // Atualiza velocidade de saída (média 30 dias) para manter rotatividade
            // correta no estoque — o caminho RegistrarSaidaEstoqueUseCase faz o mesmo.
            var agora = DateTime.UtcNow;
            const int janelaDias = 30;
            var taxaAnterior = await movRepo.GetTaxaSaidaDiariaAsync(
                pedido.EmpresaId, item.ProdutoId.Value,
                agora.AddDays(-janelaDias), agora);
            var velocidadeAtualizada = (taxaAnterior * janelaDias + qtd) / janelaDias;
            alvo.AtualizarVelocidadeSaida(velocidadeAtualizada, agora);

            await itemEstoqueRepo.UpdateAsync(alvo);

            await movRepo.InsertAsync(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = pedido.EmpresaId,
                ProdutoId = item.ProdutoId.Value,
                ItemEstoqueId = alvo.Id,
                Tipo = TipoMovimentacaoEstoque.Saida,
                Natureza = NaturezaMovimentacaoEstoque.Venda,
                Quantidade = EasyStock.Domain.ValueObjects.Quantidade.From(qtd),
                ValorUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario),
                ValorTotal = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario * qtd),
                DocumentoReferencia = refDocItem,
                DataMovimentacao = agora,
                Descricao = $"Pedido {pedido.Id} item {item.Id}",
                CriadoEm = agora
            });
        }
    }

    public async Task DevolverAsync(PedidoEntity pedido, CancellationToken ct = default)
    {
        if (!pedido.LojaId.HasValue) return;
        var lojaId = pedido.LojaId.Value;

        foreach (var item in pedido.Itens)
        {
            if (!item.ProdutoId.HasValue || item.Quantidade <= 0) continue;

            // Mesma chave de idempotência do desconto (refDocItem).
            var refDocItem = $"{pedido.Id}:{item.Id}";

            // Só devolve se houve saída anterior por este pedido+item.
            if (!await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDocItem, NaturezaMovimentacaoEstoque.Venda, ct))
                continue;

            // Idempotência do estorno: se já há um Estorno referenciando este pedido+item, pula.
            if (await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDocItem, NaturezaMovimentacaoEstoque.Estorno, ct))
                continue;

            var itens = await itemEstoqueRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value);
            var alvoCandidate = itens
                ?.Where(i => i.LojaId == lojaId)
                .OrderBy(i => i.ValidadeEm ?? DateTime.MaxValue)
                .FirstOrDefault();
            if (alvoCandidate is null) continue;

            var alvo = await itemEstoqueRepo.GetByIdComLockAsync(pedido.EmpresaId, alvoCandidate.Id) ?? alvoCandidate;

            var qtd = item.Quantidade;
            if (qtd <= 0m) continue;
            var atual = alvo.QuantidadeAtual?.Value ?? 0m;
            alvo.QuantidadeAtual = EasyStock.Domain.ValueObjects.Quantidade.From(atual + qtd);

            var agora = DateTime.UtcNow;
            const int janelaDias = 30;
            var taxaAnterior = await movRepo.GetTaxaSaidaDiariaAsync(
                pedido.EmpresaId, item.ProdutoId.Value,
                agora.AddDays(-janelaDias), agora);
            var velocidadeAtualizada = Math.Max(0m, (taxaAnterior * janelaDias - qtd) / janelaDias);
            alvo.AtualizarVelocidadeSaida(velocidadeAtualizada, agora);

            await itemEstoqueRepo.UpdateAsync(alvo);

            await movRepo.InsertAsync(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = pedido.EmpresaId,
                ProdutoId = item.ProdutoId.Value,
                ItemEstoqueId = alvo.Id,
                Tipo = TipoMovimentacaoEstoque.Entrada,
                Natureza = NaturezaMovimentacaoEstoque.Estorno,
                Quantidade = EasyStock.Domain.ValueObjects.Quantidade.From(qtd),
                ValorUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario),
                ValorTotal = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario * qtd),
                DocumentoReferencia = refDocItem,
                DataMovimentacao = agora,
                Descricao = $"Cancelamento pedido {pedido.Id} item {item.Id}",
                CriadoEm = agora
            });
        }
    }
}
