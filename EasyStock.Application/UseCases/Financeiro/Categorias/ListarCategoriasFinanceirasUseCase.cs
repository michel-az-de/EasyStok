using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Categorias;

public sealed record ListarCategoriasFinanceirasQuery(
    Guid EmpresaId,
    bool? Ativa = null,
    TipoCategoriaFinanceira? Tipo = null);

public class ListarCategoriasFinanceirasUseCase(ICategoriaFinanceiraRepository repo)
{
    public async Task<IReadOnlyList<CategoriaFinanceiraResult>> ExecuteAsync(ListarCategoriasFinanceirasQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var itens = await repo.ListarAsync(q.EmpresaId, q.Ativa, q.Tipo, ct);
        return itens.Select(CategoriaFinanceiraResult.De).ToList();
    }
}
