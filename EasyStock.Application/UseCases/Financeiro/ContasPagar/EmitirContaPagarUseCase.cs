using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Application.UseCases.Financeiro.ContasPagar;

public sealed record EmitirContaPagarCommand(
    Guid EmpresaId,
    Guid Id,
    Guid? UserId = null,
    string? UserNome = null);

public class EmitirContaPagarUseCase(
    IContaPagarRepository repo,
    IUnitOfWork uow)
{
    public async Task<ContaPagarResult?> ExecuteAsync(EmitirContaPagarCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return null;

        try { c.Emitir(); }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }

        await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaPagar(
            c.EmpresaId, c.Id, TipoEventoContaFinanceira.Emitida,
            descricao: "Conta emitida.",
            usuarioId: cmd.UserId, usuarioNome: cmd.UserNome, origem: "api"), ct);

        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return ContaPagarResult.De(c);
    }
}
