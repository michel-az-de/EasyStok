using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.ContasReceber;

public sealed record AdicionarParcelaContaReceberCommand(
    Guid EmpresaId,
    Guid ContaReceberId,
    int Numero,
    decimal Valor,
    DateTime DataVencimento,
    string? MetodoPlanejado = null);

public class AdicionarParcelaContaReceberUseCase(IContaReceberRepository repo, IUnitOfWork uow)
{
    public async Task<ContaReceberResult?> ExecuteAsync(AdicionarParcelaContaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ContaReceberId, ct);
        if (c is null) return null;

        try { c.AdicionarParcela(cmd.Numero, cmd.Valor, DataUtc.ParaUtc(cmd.DataVencimento), cmd.MetodoPlanejado); }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }

        await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
            c.EmpresaId, c.Id, TipoEventoContaFinanceira.ParcelaAdicionada,
            descricao: $"Parcela {cmd.Numero} adicionada (R$ {cmd.Valor:F2}).",
            origem: "api"), ct);
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return ContaReceberResult.De(c);
    }
}

public sealed record RemoverParcelaContaReceberCommand(Guid EmpresaId, Guid ContaReceberId, Guid ParcelaId);

public class RemoverParcelaContaReceberUseCase(IContaReceberRepository repo, IUnitOfWork uow)
{
    public async Task<ContaReceberResult?> ExecuteAsync(RemoverParcelaContaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ContaReceberId, ct);
        if (c is null) return null;

        try { c.RemoverParcela(cmd.ParcelaId); }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }

        await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
            c.EmpresaId, c.Id, TipoEventoContaFinanceira.ParcelaRemovida,
            descricao: "Parcela removida.", origem: "api"), ct);
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return ContaReceberResult.De(c);
    }
}
