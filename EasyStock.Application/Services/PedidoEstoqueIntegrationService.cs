using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.Services;

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
    ILogger<PedidoEstoqueIntegrationService> logger)
{
    public async Task DescontarAsync(PedidoEntity pedido, CancellationToken ct = default)
    {
        if (!pedido.LojaId.HasValue) { logger.LogDebug("Pedido {Id} sem LojaId — sem desconto de estoque.", pedido.Id); return; }
        var lojaId = pedido.LojaId.Value;
        var refDoc = pedido.Id.ToString();

        foreach (var item in pedido.Itens)
        {
            if (!item.ProdutoId.HasValue || item.Quantidade <= 0) continue;

            // Idempotência: se já há movimentação de Venda referenciando este pedido para
            // este produto, não duplica.
            if (await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDoc, NaturezaMovimentacaoEstoque.Venda, ct))
            {
                logger.LogDebug("Pedido {Id} item {ProdId}: movimentação já registrada (idempotência).", pedido.Id, item.ProdutoId);
                continue;
            }

            var itens = await itemEstoqueRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value);
            var alvo = itens
                ?.Where(i => i.LojaId == lojaId)
                .OrderBy(i => i.ValidadeEm ?? DateTime.MaxValue)
                .FirstOrDefault();
            if (alvo is null)
            {
                logger.LogWarning("Pedido {Id}: produto {ProdId} sem ItemEstoque na loja {LojaId} — ignorando desconto.",
                    pedido.Id, item.ProdutoId, lojaId);
                continue;
            }

            var qtdInt = (int)Math.Ceiling(item.Quantidade);
            var atual = alvo.QuantidadeAtual?.Value ?? 0;
            alvo.QuantidadeAtual = EasyStock.Domain.ValueObjects.Quantidade.From(Math.Max(0, atual - qtdInt));
            await itemEstoqueRepo.UpdateAsync(alvo);

            await movRepo.InsertAsync(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = pedido.EmpresaId,
                ProdutoId = item.ProdutoId.Value,
                ItemEstoqueId = alvo.Id,
                Tipo = TipoMovimentacaoEstoque.Saida,
                Natureza = NaturezaMovimentacaoEstoque.Venda,
                Quantidade = EasyStock.Domain.ValueObjects.Quantidade.From(qtdInt),
                ValorUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario),
                ValorTotal = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario * qtdInt),
                DocumentoReferencia = refDoc,
                DataMovimentacao = DateTime.UtcNow,
                Descricao = $"Pedido {pedido.Id}",
                CriadoEm = DateTime.UtcNow
            });
        }
    }

    public async Task DevolverAsync(PedidoEntity pedido, CancellationToken ct = default)
    {
        if (!pedido.LojaId.HasValue) return;
        var lojaId = pedido.LojaId.Value;
        var refDoc = pedido.Id.ToString();

        foreach (var item in pedido.Itens)
        {
            if (!item.ProdutoId.HasValue || item.Quantidade <= 0) continue;

            // Só devolve se houve saída anterior por este pedido.
            if (!await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDoc, NaturezaMovimentacaoEstoque.Venda, ct))
                continue;

            // Idempotência do estorno: se já há um Estorno referenciando este pedido para o produto, pula.
            if (await movRepo.ExisteReferenciaAsync(pedido.EmpresaId, item.ProdutoId.Value, refDoc, NaturezaMovimentacaoEstoque.Estorno, ct))
                continue;

            var itens = await itemEstoqueRepo.GetByProdutoAsync(pedido.EmpresaId, item.ProdutoId.Value);
            var alvo = itens
                ?.Where(i => i.LojaId == lojaId)
                .OrderBy(i => i.ValidadeEm ?? DateTime.MaxValue)
                .FirstOrDefault();
            if (alvo is null) continue;

            var qtdInt = (int)Math.Ceiling(item.Quantidade);
            var atual = alvo.QuantidadeAtual?.Value ?? 0;
            alvo.QuantidadeAtual = EasyStock.Domain.ValueObjects.Quantidade.From(atual + qtdInt);
            await itemEstoqueRepo.UpdateAsync(alvo);

            await movRepo.InsertAsync(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(),
                EmpresaId = pedido.EmpresaId,
                ProdutoId = item.ProdutoId.Value,
                ItemEstoqueId = alvo.Id,
                Tipo = TipoMovimentacaoEstoque.Entrada,
                Natureza = NaturezaMovimentacaoEstoque.Estorno,
                Quantidade = EasyStock.Domain.ValueObjects.Quantidade.From(qtdInt),
                ValorUnitario = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario),
                ValorTotal = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(item.PrecoUnitario * qtdInt),
                DocumentoReferencia = refDoc,
                DataMovimentacao = DateTime.UtcNow,
                Descricao = $"Cancelamento pedido {pedido.Id}",
                CriadoEm = DateTime.UtcNow
            });
        }
    }
}
