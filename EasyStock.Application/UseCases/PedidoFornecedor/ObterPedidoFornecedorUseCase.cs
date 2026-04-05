using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.PedidoFornecedor;

public sealed record ObterPedidoFornecedorQuery(Guid EmpresaId, Guid PedidoId);

public class ObterPedidoFornecedorUseCase(IPedidoFornecedorRepository pedidoFornecedorRepository)
{
    public async Task<PedidoFornecedorResult> ExecuteAsync(ObterPedidoFornecedorQuery query)
    {
        var pedido = await pedidoFornecedorRepository.GetByIdComItensAsync(query.PedidoId)
            ?? throw new UseCaseValidationException("Pedido de fornecedor nao encontrado.");

        if (pedido.EmpresaId != query.EmpresaId)
            throw new UseCaseValidationException("Pedido de fornecedor nao encontrado.");

        return CriarPedidoFornecedorUseCase.MapToResult(pedido, pedido.Fornecedor?.Nome, pedido.Itens);
    }
}
