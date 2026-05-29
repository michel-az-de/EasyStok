using EasyStock.Application.UseCases.Cliente;
using EasyStock.Application.UseCases.CriarCliente;

namespace EasyStock.Application.UseCases.ListarClientes;

public sealed record ListarClientesQuery(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 20,
    bool? Ativo = null,
    string? Search = null,
    string? Sort = "nome",
    string? Order = "asc");

public sealed record ListarClientesResult(
    IReadOnlyList<ClienteResult> Items,
    int Total,
    int Page,
    int PageSize);

public class ListarClientesUseCase(IClienteRepository repo)
{
    public async Task<ListarClientesResult> ExecuteAsync(ListarClientesQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (items, total) = await repo.GetByEmpresaAsync(
            q.EmpresaId, page, size, q.Ativo, q.Search, q.Sort, q.Order);

        return new ListarClientesResult(
            items.Select(CriarClienteUseCase.Map).ToList(),
            total, page, size);
    }
}
