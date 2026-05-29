using EasyStock.Application.UseCases.Financeiro.Common;

namespace EasyStock.Application.UseCases.Financeiro.ContasPagar;

public sealed record ObterContaPagarDetalheQuery(Guid EmpresaId, Guid Id);

public class ObterContaPagarDetalheUseCase(IContaPagarRepository repo)
{
    public async Task<ContaPagarResult?> ExecuteAsync(ObterContaPagarDetalheQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var c = await repo.GetByIdWithDetailsAsync(q.EmpresaId, q.Id, ct);
        return c is null ? null : ContaPagarResult.De(c);
    }
}
