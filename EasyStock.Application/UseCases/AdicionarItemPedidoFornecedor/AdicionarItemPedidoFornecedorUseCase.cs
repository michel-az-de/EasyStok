using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AdicionarItemPedidoFornecedor;

/// <summary>
/// Adiciona um <see cref="PedidoFornecedorItem"/> a um PedidoFornecedor existente.
/// CriarPedidoFornecedorUseCase cria o PF vazio; este UC popula itens depois.
/// Usado tambem pela calculadora de producao ao gerar lista de compras.
/// NAO chama Commit — caller decide (ex: CriarSugestaoCompra usa ExecuteInTransactionAsync).
/// </summary>
public class AdicionarItemPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoRepository,
    ILogger<AdicionarItemPedidoFornecedorUseCase> logger)
{
    public async Task<AdicionarItemPedidoFornecedorResult> ExecuteAsync(
        AdicionarItemPedidoFornecedorCommand command, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(command.PedidoFornecedorId, "PedidoFornecedorId");

        if (string.IsNullOrWhiteSpace(command.Nome))
            throw new UseCaseValidationException("INVALID_NAME", "Nome do item e obrigatorio.");
        if (command.Quantidade <= 0)
            throw new UseCaseValidationException("INVALID_QUANTITY", "Quantidade deve ser maior que zero.");
        if (command.CustoUnitario < 0)
            throw new UseCaseValidationException("INVALID_COST", "CustoUnitario nao pode ser negativo.");

        var pedido = await pedidoRepository.GetByIdAsync(command.PedidoFornecedorId)
            ?? throw new UseCaseValidationException("PEDIDO_NOT_FOUND", "Pedido nao encontrado.");

        if (pedido.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("CROSS_TENANT", "Pedido pertence a outra empresa.");

        var item = new PedidoFornecedorItem
        {
            Id = Guid.NewGuid(),
            PedidoFornecedorId = command.PedidoFornecedorId,
            ProdutoId = command.ProdutoId,
            Nome = command.Nome,
            Unidade = command.Unidade,
            Quantidade = command.Quantidade,
            QuantidadeRecebida = 0m,
            CustoUnitario = command.CustoUnitario,
            Observacao = command.Observacao,
            CriadoEm = DateTime.UtcNow
        };

        await pedidoRepository.AddItemAsync(item);

        logger.LogInformation(
            "Item {ItemId} adicionado ao pedido {PedidoId} (empresa {EmpresaId}, produto {ProdutoId}, qtd {Qtd}).",
            item.Id, command.PedidoFornecedorId, command.EmpresaId, command.ProdutoId, command.Quantidade);

        return new AdicionarItemPedidoFornecedorResult(item.Id);
    }
}
