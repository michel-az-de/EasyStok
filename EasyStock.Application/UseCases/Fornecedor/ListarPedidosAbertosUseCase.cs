using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.Fornecedor;

public sealed record ListarPedidosAbertosQuery(Guid EmpresaId);

public sealed record PedidoAbertoResult(
    Guid PedidoId,
    DateTime DataPedido,
    DateTime? PrevisaoEntrega,
    decimal? ValorEstimado,
    StatusPedidoFornecedor Status,
    string? Canal,
    string? Tracking,
    string? Observacoes,
    Guid FornecedorId,
    string FornecedorNome);

public class ListarPedidosAbertosUseCase(IPedidoFornecedorRepository pedidoFornecedorRepository)
{
    public async Task<IReadOnlyCollection<PedidoAbertoResult>> ExecuteAsync(ListarPedidosAbertosQuery query)
    {
        var pedidos = await pedidoFornecedorRepository.GetPedidosAbertosComFornecedorAsync(query.EmpresaId);

        return pedidos
            .OrderByDescending(p => p.DataPedido)
            .Select(p => new PedidoAbertoResult(
                p.Id,
                p.DataPedido,
                p.PrevisaoEntrega,
                p.ValorEstimado,
                p.Status,
                p.Canal,
                p.Tracking,
                p.Observacoes,
                p.FornecedorId,
                p.Fornecedor?.Nome ?? "—"))
            .ToArray();
    }
}
