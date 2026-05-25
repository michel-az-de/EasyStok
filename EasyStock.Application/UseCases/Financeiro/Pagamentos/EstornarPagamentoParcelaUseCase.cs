using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Financeiro.Pagamentos;

public sealed record EstornarPagamentoParcelaPagarCommand(
    Guid EmpresaId,
    Guid ParcelaId,
    Guid PagamentoId,
    string? Motivo,
    Guid? UserId = null,
    string? UserNome = null);

public class EstornarPagamentoParcelaPagarUseCase(
    IContaPagarRepository contaRepo,
    ICaixaRepository caixaRepo,
    IUnitOfWork uow,
    ILogger<EstornarPagamentoParcelaPagarUseCase> logger)
{
    public async Task<bool> ExecuteAsync(EstornarPagamentoParcelaPagarCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var parcela = await contaRepo.GetParcelaWithContaAsync(cmd.EmpresaId, cmd.ParcelaId, ct);
        if (parcela is null) return false;
        var conta = parcela.ContaPagar
                    ?? throw new UseCaseValidationException("Parcela orfa.");
        var pag = parcela.Pagamentos.FirstOrDefault(p => p.Id == cmd.PagamentoId);
        if (pag is null) return false;

        try
        {
            pag.Estornar(cmd.UserId, cmd.Motivo);

            // Estornar MovimentoCaixa correspondente (mesma transacao)
            if (pag.MovimentoCaixaId.HasValue)
            {
                var mov = await caixaRepo.GetMovimentoAsync(cmd.EmpresaId, pag.MovimentoCaixaId.Value);
                if (mov is not null && mov.EstornadoEm is null)
                {
                    mov.Estornar(cmd.UserId, cmd.UserNome,
                        cmd.Motivo ?? $"Estorno pagamento parcela {parcela.Numero}");
                    await caixaRepo.UpdateMovimentoAsync(mov);
                }
            }

            parcela.AtualizarStatusPorPagamentos();
            conta.AtualizarStatusPorParcelas();

            await contaRepo.AddEventoAsync(ContaFinanceiraEvento.ParaContaPagar(
                conta.EmpresaId, conta.Id, TipoEventoContaFinanceira.PagamentoEstornado,
                descricao: cmd.Motivo,
                valorDepois: $"-R$ {pag.Valor:F2}",
                usuarioId: cmd.UserId, usuarioNome: cmd.UserNome,
                origem: "api"), ct);

            await contaRepo.UpdateAsync(conta, ct);
            await uow.CommitAsync();

            logger.LogInformation("Pagamento {PagId} estornado (parcela={ParcId}, CP={ContaId}, mov={MovId}).",
                pag.Id, parcela.Id, conta.Id, pag.MovimentoCaixaId);
            return true;
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}

public sealed record EstornarPagamentoParcelaReceberCommand(
    Guid EmpresaId,
    Guid ParcelaId,
    Guid PagamentoId,
    string? Motivo,
    Guid? UserId = null,
    string? UserNome = null);

public class EstornarPagamentoParcelaReceberUseCase(
    IContaReceberRepository contaRepo,
    ICaixaRepository caixaRepo,
    IUnitOfWork uow,
    ILogger<EstornarPagamentoParcelaReceberUseCase> logger)
{
    public async Task<bool> ExecuteAsync(EstornarPagamentoParcelaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var parcela = await contaRepo.GetParcelaWithContaAsync(cmd.EmpresaId, cmd.ParcelaId, ct);
        if (parcela is null) return false;
        var conta = parcela.ContaReceber
                    ?? throw new UseCaseValidationException("Parcela orfa.");
        var pag = parcela.Pagamentos.FirstOrDefault(p => p.Id == cmd.PagamentoId);
        if (pag is null) return false;

        try
        {
            pag.Estornar(cmd.UserId, cmd.Motivo);

            if (pag.MovimentoCaixaId.HasValue)
            {
                var mov = await caixaRepo.GetMovimentoAsync(cmd.EmpresaId, pag.MovimentoCaixaId.Value);
                if (mov is not null && mov.EstornadoEm is null)
                {
                    mov.Estornar(cmd.UserId, cmd.UserNome,
                        cmd.Motivo ?? $"Estorno recebimento parcela {parcela.Numero}");
                    await caixaRepo.UpdateMovimentoAsync(mov);
                }
            }

            parcela.AtualizarStatusPorPagamentos();
            conta.AtualizarStatusPorParcelas();

            await contaRepo.AddEventoAsync(ContaFinanceiraEvento.ParaContaReceber(
                conta.EmpresaId, conta.Id, TipoEventoContaFinanceira.PagamentoEstornado,
                descricao: cmd.Motivo,
                valorDepois: $"-R$ {pag.Valor:F2}",
                usuarioId: cmd.UserId, usuarioNome: cmd.UserNome,
                origem: "api"), ct);

            await contaRepo.UpdateAsync(conta, ct);
            await uow.CommitAsync();

            logger.LogInformation("Pagamento {PagId} (CR) estornado (parcela={ParcId}, CR={ContaId}, mov={MovId}).",
                pag.Id, parcela.Id, conta.Id, pag.MovimentoCaixaId);
            return true;
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
