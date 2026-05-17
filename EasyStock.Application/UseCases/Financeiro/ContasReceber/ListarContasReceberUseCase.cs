using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.ContasReceber;

public sealed record ListarContasReceberQuery(
    Guid EmpresaId,
    StatusContaFinanceira? Status = null,
    Guid? ClienteId = null,
    Guid? CategoriaId = null,
    Guid? CentroCustoId = null,
    DateTime? VencimentoDe = null,
    DateTime? VencimentoAte = null,
    string? Busca = null,
    int Page = 1,
    int PageSize = 20,
    string? Sort = "datavencimento",
    string? Order = "asc");

public class ListarContasReceberUseCase(IContaReceberRepository repo)
{
    public async Task<ContasReceberPaginadasResult> ExecuteAsync(ListarContasReceberQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (itens, total) = await repo.ListarAsync(
            q.EmpresaId, q.Status, q.ClienteId, q.CategoriaId, q.CentroCustoId,
            q.VencimentoDe, q.VencimentoAte, q.Busca,
            page, size, q.Sort, q.Order, ct);

        return new ContasReceberPaginadasResult(
            itens.Select(ContaReceberResult.De).ToList(),
            total, page, size);
    }
}

public sealed record ObterContaReceberDetalheQuery(Guid EmpresaId, Guid Id);

public class ObterContaReceberDetalheUseCase(IContaReceberRepository repo)
{
    public async Task<ContaReceberResult?> ExecuteAsync(ObterContaReceberDetalheQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var c = await repo.GetByIdWithDetailsAsync(q.EmpresaId, q.Id, ct);
        return c is null ? null : ContaReceberResult.De(c);
    }
}
