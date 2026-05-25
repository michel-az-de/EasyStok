using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.ContasPagar;

public sealed record ListarContasPagarQuery(
    Guid EmpresaId,
    StatusContaFinanceira? Status = null,
    Guid? FornecedorId = null,
    Guid? CategoriaId = null,
    Guid? CentroCustoId = null,
    DateTime? VencimentoDe = null,
    DateTime? VencimentoAte = null,
    string? Busca = null,
    int Page = 1,
    int PageSize = 20,
    string? Sort = "datavencimento",
    string? Order = "asc");

public class ListarContasPagarUseCase(IContaPagarRepository repo)
{
    public async Task<ContasPagarPaginadasResult> ExecuteAsync(ListarContasPagarQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (itens, total) = await repo.ListarAsync(
            q.EmpresaId, q.Status, q.FornecedorId, q.CategoriaId, q.CentroCustoId,
            q.VencimentoDe, q.VencimentoAte, q.Busca,
            page, size, q.Sort, q.Order, ct);

        return new ContasPagarPaginadasResult(
            itens.Select(ContaPagarResult.De).ToList(),
            total, page, size);
    }
}
