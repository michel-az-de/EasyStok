using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Application.UseCases.Financeiro.ContasReceber;

public sealed record EmitirContaReceberCommand(Guid EmpresaId, Guid Id, Guid? UserId = null, string? UserNome = null);

public class EmitirContaReceberUseCase(IContaReceberRepository repo, IUnitOfWork uow)
{
    public async Task<ContaReceberResult?> ExecuteAsync(EmitirContaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return null;

        try { c.Emitir(); }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }

        await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
            c.EmpresaId, c.Id, TipoEventoContaFinanceira.Emitida,
            descricao: "Conta emitida.",
            usuarioId: cmd.UserId, usuarioNome: cmd.UserNome, origem: "api"), ct);
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return ContaReceberResult.De(c);
    }
}

public sealed record CancelarContaReceberCommand(
    Guid EmpresaId, Guid Id, string Motivo,
    Guid? UserId = null, string? UserNome = null);

public class CancelarContaReceberUseCase(IContaReceberRepository repo, IUnitOfWork uow)
{
    public async Task<ContaReceberResult?> ExecuteAsync(CancelarContaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (string.IsNullOrWhiteSpace(cmd.Motivo))
            throw new UseCaseValidationException("Motivo do cancelamento e obrigatorio.");

        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return null;

        try { c.Cancelar(cmd.Motivo, cmd.UserId); }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }

        await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
            c.EmpresaId, c.Id, TipoEventoContaFinanceira.Cancelada,
            descricao: cmd.Motivo,
            usuarioId: cmd.UserId, usuarioNome: cmd.UserNome, origem: "api"), ct);
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return ContaReceberResult.De(c);
    }
}
