using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Cliente;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarCliente;

namespace EasyStock.Application.UseCases.BuscarCliente;

public sealed record BuscarClienteQuery(Guid EmpresaId, string Termo, int Max = 20);

public class BuscarClienteUseCase(IClienteRepository repo)
{
    public async Task<IReadOnlyList<ClienteResult>> ExecuteAsync(BuscarClienteQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        if (string.IsNullOrWhiteSpace(q.Termo)) return [];

        var max = Math.Clamp(q.Max, 1, 100);
        var items = await repo.SearchAsync(q.EmpresaId, q.Termo, max);
        return items.Select(CriarClienteUseCase.Map).ToList();
    }
}
