using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.ContasPagar;

public sealed record AdicionarParcelaContaPagarCommand(
    Guid EmpresaId,
    Guid ContaPagarId,
    int Numero,
    decimal Valor,
    DateTime DataVencimento,
    string? MetodoPlanejado = null);

public class AdicionarParcelaContaPagarUseCase(
    IContaPagarRepository repo,
    IUnitOfWork uow)
{
    public async Task<ContaPagarResult?> ExecuteAsync(AdicionarParcelaContaPagarCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ContaPagarId, ct);
        if (c is null) return null;

        try
        {
            c.AdicionarParcela(cmd.Numero, cmd.Valor, DataUtc.ParaUtc(cmd.DataVencimento), cmd.MetodoPlanejado);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaPagar(
            c.EmpresaId, c.Id, TipoEventoContaFinanceira.ParcelaAdicionada,
            descricao: $"Parcela {cmd.Numero} adicionada (R$ {cmd.Valor:F2}).",
            origem: "api"), ct);
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return ContaPagarResult.De(c);
    }
}

public sealed record RemoverParcelaContaPagarCommand(Guid EmpresaId, Guid ContaPagarId, Guid ParcelaId);

public class RemoverParcelaContaPagarUseCase(
    IContaPagarRepository repo,
    IUnitOfWork uow)
{
    public async Task<ContaPagarResult?> ExecuteAsync(RemoverParcelaContaPagarCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ContaPagarId, ct);
        if (c is null) return null;

        try { c.RemoverParcela(cmd.ParcelaId); }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }

        await repo.AddEventoAsync(ContaFinanceiraEvento.ParaContaPagar(
            c.EmpresaId, c.Id, TipoEventoContaFinanceira.ParcelaRemovida,
            descricao: "Parcela removida.", origem: "api"), ct);
        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return ContaPagarResult.De(c);
    }
}
