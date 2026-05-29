using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.Lotes;

namespace EasyStock.Application.UseCases.ListarLotes;

public sealed record ListarLotesQuery(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 30,
    string? Status = null,
    DateTime? Desde = null,
    DateTime? Ate = null,
    string? Search = null,
    string? Sort = "dataproducao",
    string? Order = "desc");

public sealed record ListarLotesResult(
    IReadOnlyList<LoteResult> Items,
    int Total,
    int Page,
    int PageSize);

public class ListarLotesUseCase(ILoteRepository repo)
{
    public async Task<ListarLotesResult> ExecuteAsync(ListarLotesQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (items, total) = await repo.ListAsync(q.EmpresaId, page, size,
            q.Status, q.Desde, q.Ate, q.Search, q.Sort, q.Order);

        return new ListarLotesResult(
            items.Select(CriarLoteUseCase.Map).ToList(), total, page, size);
    }
}
