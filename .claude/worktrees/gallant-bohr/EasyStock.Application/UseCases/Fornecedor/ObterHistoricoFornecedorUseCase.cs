using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fornecedor;

public sealed record ObterHistoricoFornecedorQuery(Guid EmpresaId, Guid FornecedorId);

public class ObterHistoricoFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IPedidoFornecedorRepository pedidoFornecedorRepository)
{
    public async Task<IReadOnlyCollection<FornecedorPedidoHistoricoItemResult>> ExecuteAsync(ObterHistoricoFornecedorQuery query)
    {
        var fornecedor = await fornecedorRepository.GetByIdAsync(query.EmpresaId, query.FornecedorId);
        if (fornecedor is null)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        var pedidos = await pedidoFornecedorRepository.GetHistoricoPorFornecedorAsync(query.EmpresaId, query.FornecedorId);
        return pedidos
            .OrderByDescending(p => p.DataPedido)
            .Select(p => new FornecedorPedidoHistoricoItemResult(
                p.Id,
                p.DataPedido,
                p.PrevisaoEntrega,
                p.DataRecebimento,
                p.ValorEstimado,
                p.Status,
                p.Canal,
                p.Tracking,
                p.Observacoes))
            .ToArray();
    }
}
