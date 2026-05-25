using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Financeiro.Categorias;

public sealed record InativarCategoriaFinanceiraCommand(Guid EmpresaId, Guid Id);

public class InativarCategoriaFinanceiraUseCase(
    ICategoriaFinanceiraRepository repo,
    IUnitOfWork uow)
{
    public async Task<bool> ExecuteAsync(InativarCategoriaFinanceiraCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, nameof(cmd.Id));

        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return false;
        if (!c.Ativa) return true;

        if (await repo.ExisteContaAbertaAsync(cmd.EmpresaId, cmd.Id, ct))
            throw new UseCaseValidationException("Categoria possui contas abertas — finalize-as antes de inativar.");

        c.Inativar();
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return true;
    }
}

public sealed record ReativarCategoriaFinanceiraCommand(Guid EmpresaId, Guid Id);

public class ReativarCategoriaFinanceiraUseCase(
    ICategoriaFinanceiraRepository repo,
    IUnitOfWork uow)
{
    public async Task<bool> ExecuteAsync(ReativarCategoriaFinanceiraCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return false;
        if (c.Ativa) return true;
        c.Reativar();
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return true;
    }
}
