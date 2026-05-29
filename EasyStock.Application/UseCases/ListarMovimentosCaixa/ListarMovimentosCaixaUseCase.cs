using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.UseCases.ListarMovimentosCaixa;

public sealed record ListarMovimentosCaixaQuery(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 50,
    string? Tipo = null,
    DateTime? Desde = null,
    DateTime? Ate = null,
    bool IncluirEstornados = false,
    string? Sort = "datamovimento",
    string? Order = "desc");

public sealed record ListarMovimentosCaixaResult(
    IReadOnlyList<MovimentoCaixaResult> Items,
    int Total,
    int Page,
    int PageSize);

public class ListarMovimentosCaixaUseCase(ICaixaRepository repo)
{
    public async Task<ListarMovimentosCaixaResult> ExecuteAsync(ListarMovimentosCaixaQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (items, total) = await repo.ListMovimentosAsync(
            q.EmpresaId, page, size, q.Tipo, q.Desde, q.Ate,
            q.IncluirEstornados, q.Sort, q.Order);

        return new ListarMovimentosCaixaResult(
            items.Select(AbrirCaixaUseCase.Map).ToList(),
            total, page, size);
    }
}
