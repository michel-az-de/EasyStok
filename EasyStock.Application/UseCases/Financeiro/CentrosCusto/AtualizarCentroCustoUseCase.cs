using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;

namespace EasyStock.Application.UseCases.Financeiro.CentrosCusto;

public sealed record AtualizarCentroCustoCommand(
    Guid EmpresaId,
    Guid Id,
    string? Nome = null,
    string? Descricao = null,
    Guid? LojaId = null);

public class AtualizarCentroCustoUseCase(
    ICentroCustoRepository repo,
    IUnitOfWork uow)
{
    public async Task<CentroCustoResult?> ExecuteAsync(AtualizarCentroCustoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, nameof(cmd.Id));

        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return null;

        c.Atualizar(cmd.Nome, cmd.Descricao, cmd.LojaId);
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return CentroCustoResult.De(c);
    }
}
