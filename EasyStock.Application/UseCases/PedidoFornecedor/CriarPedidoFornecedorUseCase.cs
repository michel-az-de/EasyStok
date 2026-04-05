using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.PedidoFornecedor;

public sealed record ItemPedidoFornecedorInput(
    Guid? ProdutoId,
    Guid? ProdutoVariacaoId,
    string Descricao,
    decimal Quantidade,
    decimal? CustoUnitario);

public sealed record CriarPedidoFornecedorCommand(
    Guid EmpresaId,
    Guid FornecedorId,
    DateTime? PrevisaoEntrega,
    decimal? ValorEstimado,
    string? Canal,
    string? Tracking,
    string? Observacoes,
    IReadOnlyCollection<ItemPedidoFornecedorInput> Itens);

public class CriarPedidoFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IPedidoFornecedorRepository pedidoFornecedorRepository,
    IItemPedidoFornecedorRepository itemPedidoFornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarPedidoFornecedorUseCase> logger)
{
    public async Task<PedidoFornecedorResult> ExecuteAsync(CriarPedidoFornecedorCommand command)
    {
        if (command.EmpresaId == Guid.Empty)
            throw new UseCaseValidationException("EmpresaId e obrigatorio.");

        if (command.FornecedorId == Guid.Empty)
            throw new UseCaseValidationException("FornecedorId e obrigatorio.");

        if (command.Itens is null || command.Itens.Count == 0)
            throw new UseCaseValidationException("O pedido deve conter ao menos um item.");

        foreach (var item in command.Itens)
        {
            if (string.IsNullOrWhiteSpace(item.Descricao))
                throw new UseCaseValidationException("Cada item deve ter uma descricao.");
            if (item.Quantidade <= 0)
                throw new UseCaseValidationException("A quantidade de cada item deve ser maior que zero.");
        }

        var fornecedor = await fornecedorRepository.GetByIdAsync(command.EmpresaId, command.FornecedorId)
            ?? throw new UseCaseValidationException("Fornecedor nao encontrado.");

        if (!fornecedor.Ativo)
            throw new UseCaseValidationException("Nao e permitido criar pedido para fornecedor inativo.");

        var pedido = Domain.Entities.PedidoFornecedor.Criar(
            command.EmpresaId,
            command.FornecedorId,
            command.PrevisaoEntrega,
            command.ValorEstimado,
            command.Canal,
            command.Tracking,
            command.Observacoes);

        await pedidoFornecedorRepository.AddAsync(pedido);

        var itens = command.Itens
            .Select(i => ItemPedidoFornecedor.Criar(
                pedido.Id,
                command.EmpresaId,
                i.ProdutoId,
                i.ProdutoVariacaoId,
                i.Descricao,
                i.Quantidade,
                i.CustoUnitario))
            .ToList();

        await itemPedidoFornecedorRepository.AddRangeAsync(itens);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Pedido de fornecedor {PedidoId} criado para empresa {EmpresaId}.", pedido.Id, pedido.EmpresaId);

        return MapToResult(pedido, fornecedor.Nome, itens);
    }

    internal static PedidoFornecedorResult MapToResult(
        Domain.Entities.PedidoFornecedor pedido,
        string? fornecedorNome,
        IEnumerable<ItemPedidoFornecedor> itens) =>
        new(
            pedido.Id,
            pedido.EmpresaId,
            pedido.FornecedorId,
            fornecedorNome,
            pedido.DataPedido,
            pedido.PrevisaoEntrega,
            pedido.DataRecebimento,
            pedido.ValorEstimado,
            pedido.Status,
            pedido.Canal,
            pedido.Tracking,
            pedido.Observacoes,
            itens.Select(i => new ItemPedidoFornecedorResult(
                i.Id,
                i.ProdutoId,
                i.ProdutoVariacaoId,
                i.Descricao,
                i.Quantidade,
                i.CustoUnitario)).ToArray());
}
