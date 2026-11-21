using EasyStock.Application.UseCases.Cliente;
using EasyStock.Application.UseCases.CriarCliente;

namespace EasyStock.Application.UseCases.BuscarCliente;

/// <summary>#2780: <c>IncluirInativos</c> só para quem procura um cadastro (Buscar na Casa); quem escolhe cliente para agir não recebe inativo.</summary>
public sealed record BuscarClienteQuery(Guid EmpresaId, string Termo, int Max = 20, bool IncluirInativos = false);

public class BuscarClienteUseCase(IClienteRepository repo)
{
    public async Task<IReadOnlyList<ClienteResult>> ExecuteAsync(BuscarClienteQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        if (string.IsNullOrWhiteSpace(q.Termo)) return [];

        var max = Math.Clamp(q.Max, 1, 100);
        var items = await repo.SearchAsync(q.EmpresaId, q.Termo, max, q.IncluirInativos);
        return items.Select(CriarClienteUseCase.Map).ToList();
    }
}
