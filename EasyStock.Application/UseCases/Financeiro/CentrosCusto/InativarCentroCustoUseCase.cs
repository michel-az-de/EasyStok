namespace EasyStock.Application.UseCases.Financeiro.CentrosCusto;

public sealed record InativarCentroCustoCommand(Guid EmpresaId, Guid Id);

public class InativarCentroCustoUseCase(
    ICentroCustoRepository repo,
    IUnitOfWork uow)
{
    public async Task<bool> ExecuteAsync(InativarCentroCustoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return false;
        if (!c.Ativo) return true;
        if (await repo.ExisteContaAbertaAsync(cmd.EmpresaId, cmd.Id, ct))
            throw new UseCaseValidationException("Centro de custo possui contas abertas — finalize-as antes de inativar.");

        c.Inativar();
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return true;
    }
}

public sealed record ReativarCentroCustoCommand(Guid EmpresaId, Guid Id);

public class ReativarCentroCustoUseCase(
    ICentroCustoRepository repo,
    IUnitOfWork uow)
{
    public async Task<bool> ExecuteAsync(ReativarCentroCustoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return false;
        if (c.Ativo) return true;
        c.Reativar();
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return true;
    }
}
