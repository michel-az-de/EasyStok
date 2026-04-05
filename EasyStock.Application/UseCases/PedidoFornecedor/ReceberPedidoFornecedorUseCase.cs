using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using FornecedorEntity = EasyStock.Domain.Entities.Fornecedor;

namespace EasyStock.Application.UseCases.PedidoFornecedor;

public sealed record ReceberPedidoFornecedorCommand(
    Guid PedidoId,
    Guid EmpresaId);

public class ReceberPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoFornecedorRepository,
    IFornecedorRepository fornecedorRepository,
    IProdutoRepository produtoRepository,
    IProdutoVariacaoRepository produtoVariacaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
    INotificacaoRepository notificacaoRepository,
    IUnitOfWork unitOfWork,
    ILogger<ReceberPedidoFornecedorUseCase> logger)
{
    public async Task<PedidoFornecedorResult> ExecuteAsync(ReceberPedidoFornecedorCommand command)
    {
        var pedido = await pedidoFornecedorRepository.GetByIdComItensAsync(command.PedidoId)
            ?? throw new UseCaseValidationException("Pedido de fornecedor nao encontrado.");

        if (pedido.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Pedido de fornecedor nao encontrado.");

        try
        {
            pedido.Receber();
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        var fornecedor = await fornecedorRepository.GetByIdAsync(pedido.EmpresaId, pedido.FornecedorId)
            ?? throw new UseCaseValidationException("Fornecedor associado ao pedido nao encontrado.");

        var agora = DateTime.UtcNow;

        foreach (var item in pedido.Itens.Where(i => i.ProdutoId.HasValue))
        {
            var produto = await produtoRepository.GetByIdAsync(item.ProdutoId!.Value);
            if (produto is null || produto.EmpresaId != command.EmpresaId)
            {
                logger.LogWarning(
                    "Item {ItemId} do pedido {PedidoId} possui ProdutoId {ProdutoId} que nao foi encontrado. Entrada ignorada.",
                    item.Id, pedido.Id, item.ProdutoId);
                continue;
            }

            ProdutoVariacao? variacao = null;
            if (item.ProdutoVariacaoId.HasValue)
            {
                variacao = await produtoVariacaoRepository.GetByIdAsync(item.ProdutoVariacaoId.Value);
                if (variacao is null || variacao.ProdutoId != produto.Id)
                {
                    logger.LogWarning(
                        "Variacao {VariacaoId} do item {ItemId} nao encontrada ou nao pertence ao produto. Entrada ignorada.",
                        item.ProdutoVariacaoId, item.Id);
                    continue;
                }
            }

            var quantidade = Quantidade.From((int)Math.Max(1, Math.Round(item.Quantidade)));
            var custoUnitario = item.CustoUnitario.HasValue
                ? Dinheiro.FromDecimal(item.CustoUnitario.Value)
                : Dinheiro.FromDecimal(0m);

            var itemEstoque = ItemEstoque.CriarParaEntrada(
                Guid.NewGuid(),
                command.EmpresaId,
                produto,
                variacao,
                quantidade,
                custoUnitario,
                null,
                pedido.DataRecebimento!.Value,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                fornecedor.Nome,
                null,
                $"Recebimento do pedido {pedido.Id}",
                agora);

            var movimentacao = MovimentacaoEstoque.CriarEntrada(
                Guid.NewGuid(),
                command.EmpresaId,
                itemEstoque,
                NaturezaMovimentacaoEstoque.Compra,
                quantidade,
                custoUnitario,
                pedido.DataRecebimento!.Value,
                $"Pedido de fornecedor {pedido.Id}",
                pedido.Id.ToString(),
                agora);

            await itemEstoqueRepository.InsertAsync(itemEstoque);
            await movimentacaoEstoqueRepository.InsertAsync(movimentacao);
        }

        await pedidoFornecedorRepository.UpdateAsync(pedido);

        RecalcularLeadTimeReal(pedido, fornecedor);
        await fornecedorRepository.UpdateAsync(fornecedor);

        var notificacao = Notificacao.Criar(
            command.EmpresaId,
            TipoAlertaEstoque.PedidoRecebido,
            $"Pedido do fornecedor '{fornecedor.Nome}' foi recebido.",
            pedido.Id);
        await notificacaoRepository.AddAsync(notificacao);

        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Pedido {PedidoId} do fornecedor {FornecedorNome} recebido. LeadTime real atualizado para {LeadTime} dias.",
            pedido.Id, fornecedor.Nome, fornecedor.LeadTimeRealMedioDias);

        return CriarPedidoFornecedorUseCase.MapToResult(pedido, fornecedor.Nome, pedido.Itens);
    }

    private static void RecalcularLeadTimeReal(
        Domain.Entities.PedidoFornecedor pedidoRecebido,
        FornecedorEntity fornecedor)
    {
        var leadTimeDias = (decimal)(pedidoRecebido.DataRecebimento!.Value.Date - pedidoRecebido.DataPedido.Date).TotalDays;

        if (fornecedor.LeadTimeRealMedioDias is null)
        {
            fornecedor.AtualizarLeadTimeReal(decimal.Round(leadTimeDias, 2));
        }
        else
        {
            // Rolling average: keep existing mean and blend with the new observation
            var novaMedia = decimal.Round((fornecedor.LeadTimeRealMedioDias.Value + leadTimeDias) / 2m, 2);
            fornecedor.AtualizarLeadTimeReal(novaMedia);
        }
    }
}
