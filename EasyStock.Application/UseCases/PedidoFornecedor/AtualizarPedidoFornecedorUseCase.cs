using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.PedidoFornecedor;

public sealed record AtualizarPedidoFornecedorCommand(
    Guid PedidoId,
    Guid EmpresaId,
    DateTime? PrevisaoEntrega,
    decimal? ValorEstimado,
    string? Canal,
    string? Tracking,
    string? Observacoes,
    IReadOnlyCollection<ItemPedidoFornecedorInput> Itens);

public class AtualizarPedidoFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IPedidoFornecedorRepository pedidoFornecedorRepository,
    IItemPedidoFornecedorRepository itemPedidoFornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<AtualizarPedidoFornecedorUseCase> logger)
{
    public async Task<PedidoFornecedorResult> ExecuteAsync(AtualizarPedidoFornecedorCommand command)
    {
        var pedido = await pedidoFornecedorRepository.GetByIdComItensAsync(command.PedidoId)
            ?? throw new UseCaseValidationException("Pedido de fornecedor nao encontrado.");

        if (pedido.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Pedido de fornecedor nao encontrado.");

        if (pedido.Status == Domain.Enums.StatusPedidoFornecedor.Recebido ||
            pedido.Status == Domain.Enums.StatusPedidoFornecedor.Cancelado)
            throw new UseCaseValidationException("Nao e possivel editar um pedido ja recebido ou cancelado.");

        if (command.Itens is null || command.Itens.Count == 0)
            throw new UseCaseValidationException("O pedido deve conter ao menos um item.");

        foreach (var item in command.Itens)
        {
            if (string.IsNullOrWhiteSpace(item.Descricao))
                throw new UseCaseValidationException("Cada item deve ter uma descricao.");
            if (item.Quantidade <= 0)
                throw new UseCaseValidationException("A quantidade de cada item deve ser maior que zero.");
        }

        pedido.Atualizar(
            command.PrevisaoEntrega,
            command.ValorEstimado,
            command.Canal,
            command.Tracking,
            command.Observacoes);

        await pedidoFornecedorRepository.UpdateAsync(pedido);

        await itemPedidoFornecedorRepository.RemoveByPedidoAsync(pedido.Id);

        var novosItens = command.Itens
            .Select(i => ItemPedidoFornecedor.Criar(
                pedido.Id,
                pedido.EmpresaId,
                i.ProdutoId,
                i.ProdutoVariacaoId,
                i.Descricao,
                i.Quantidade,
                i.CustoUnitario))
            .ToList();

        await itemPedidoFornecedorRepository.AddRangeAsync(novosItens);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Pedido de fornecedor {PedidoId} atualizado.", pedido.Id);

        var fornecedor = await fornecedorRepository.GetByIdAsync(pedido.EmpresaId, pedido.FornecedorId);
        return CriarPedidoFornecedorUseCase.MapToResult(pedido, fornecedor?.Nome, novosItens);
    }
}
