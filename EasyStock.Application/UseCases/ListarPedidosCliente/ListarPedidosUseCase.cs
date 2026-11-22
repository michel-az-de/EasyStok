using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Application.UseCases.Pedidos;

namespace EasyStock.Application.UseCases.ListarPedidosCliente;

public sealed record ListarPedidosQuery(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? ClienteId = null,
    DateTime? Desde = null,
    DateTime? Ate = null,
    string? Search = null,
    string? Sort = "criadoem",
    string? Order = "desc",
    // E4 L2 (#3076): dia da entrega = AgendadoParaEm, ou CriadoEm no pedido para agora.
    DateTime? EntregaDesde = null,
    DateTime? EntregaAte = null);

public sealed record ListarPedidosResult(
    IReadOnlyList<PedidoResult> Items,
    int Total,
    int Page,
    int PageSize);

public class ListarPedidosUseCase(IPedidoRepository repo)
{
    public async Task<ListarPedidosResult> ExecuteAsync(ListarPedidosQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (items, total) = await repo.GetByEmpresaAsync(
            q.EmpresaId, page, size,
            q.Status, q.ClienteId, q.Desde, q.Ate,
            q.Search, q.Sort, q.Order, q.EntregaDesde, q.EntregaAte);

        return new ListarPedidosResult(
            items.Select(CriarPedidoUseCase.Map).ToList(),
            total, page, size);
    }
}
