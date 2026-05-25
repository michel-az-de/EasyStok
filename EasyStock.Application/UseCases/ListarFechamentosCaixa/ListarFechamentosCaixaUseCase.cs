using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.FecharCaixa;

namespace EasyStock.Application.UseCases.ListarFechamentosCaixa;

public sealed record ListarFechamentosCaixaQuery(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 30,
    DateOnly? Desde = null,
    DateOnly? Ate = null);

public sealed record ListarFechamentosCaixaResult(
    IReadOnlyList<FechamentoCaixaResult> Items,
    int Total,
    int Page,
    int PageSize);

public class ListarFechamentosCaixaUseCase(ICaixaRepository repo)
{
    public async Task<ListarFechamentosCaixaResult> ExecuteAsync(ListarFechamentosCaixaQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (items, total) = await repo.ListFechamentosAsync(q.EmpresaId, page, size, q.Desde, q.Ate);

        return new ListarFechamentosCaixaResult(
            items.Select(FecharCaixaUseCase.Map).ToList(),
            total, page, size);
    }
}
