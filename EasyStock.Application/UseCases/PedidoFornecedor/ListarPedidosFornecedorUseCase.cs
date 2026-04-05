using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.PedidoFornecedor;

public sealed record ListarPedidosFornecedorQuery(
    Guid EmpresaId,
    Guid? FornecedorId = null,
    StatusPedidoFornecedor? Status = null,
    int Page = 1,
    int PageSize = 20);

public class ListarPedidosFornecedorUseCase(IPedidoFornecedorRepository pedidoFornecedorRepository)
{
    public async Task<(IEnumerable<PedidoFornecedorResult> Pedidos, int Total)> ExecuteAsync(ListarPedidosFornecedorQuery query)
    {
        if (query.EmpresaId == Guid.Empty)
            throw new UseCaseValidationException("EmpresaId e obrigatorio.");

        var (pedidos, total) = await pedidoFornecedorRepository.GetPedidosPaginadosAsync(
            query.EmpresaId,
            query.FornecedorId,
            query.Status,
            query.Page,
            query.PageSize);

        var results = pedidos.Select(p => CriarPedidoFornecedorUseCase.MapToResult(
            p,
            p.Fornecedor?.Nome,
            p.Itens));

        return (results, total);
    }
}
