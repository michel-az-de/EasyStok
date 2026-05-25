using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;

namespace EasyStock.Application.UseCases.Financeiro.CentrosCusto;

public sealed record ListarCentrosCustoQuery(Guid EmpresaId, bool? Ativo = null, Guid? LojaId = null);

public class ListarCentrosCustoUseCase(ICentroCustoRepository repo)
{
    public async Task<IReadOnlyList<CentroCustoResult>> ExecuteAsync(ListarCentrosCustoQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var itens = await repo.ListarAsync(q.EmpresaId, q.Ativo, q.LojaId, ct);
        return itens.Select(CentroCustoResult.De).ToList();
    }
}
