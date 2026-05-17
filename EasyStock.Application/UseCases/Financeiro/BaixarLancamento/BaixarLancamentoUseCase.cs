using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Financeiro;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Financeiro.BaixarLancamento;

public sealed record BaixarLancamentoCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid LancamentoId,
    [property: Range(typeof(decimal), "0.01", "999999999")] decimal Valor,
    DateTime DataBaixa,
    [property: Required][property: MaxLength(20)] string MeioPagamento,
    Guid? ContaBancariaId = null,
    [property: MaxLength(120)] string? ChaveExterna = null,
    [property: MaxLength(500)] string? Observacao = null,
    Guid? RegistradoPorUserId = null,
    [property: MaxLength(120)] string? RegistradoPorNome = null);

public sealed record BaixarLancamentoResult(
    Guid LancamentoId,
    Guid BaixaId,
    decimal ValorBaixado,
    decimal ValorRestante,
    StatusLancamento StatusResultante);

/// <summary>
/// Aplica baixa (pagamento) total ou parcial a um lancamento financeiro.
/// Idempotente quando <see cref="BaixarLancamentoCommand.ChaveExterna"/> e
/// reapresentada — mesma chave + mesmo lancamento devolve a baixa preexistente
/// sem duplicar. Usa lock pessimista para serializar baixas concorrentes.
/// </summary>
public sealed class BaixarLancamentoUseCase(
    ILancamentoRepository repo,
    IUnitOfWork uow,
    ILogger<BaixarLancamentoUseCase> logger,
    IPublicadorEventos? publicadorEventos = null)
{
    public async Task<BaixarLancamentoResult> ExecuteAsync(
        BaixarLancamentoCommand cmd,
        CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.LancamentoId, nameof(cmd.LancamentoId));

        if (cmd.Valor <= 0m)
            throw new UseCaseValidationException("Valor da baixa deve ser maior que zero.");
        if (string.IsNullOrWhiteSpace(cmd.MeioPagamento))
            throw new UseCaseValidationException("MeioPagamento obrigatorio.");

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var lancamento = await repo.GetWithLockAsync(cmd.EmpresaId, cmd.LancamentoId, token)
                ?? throw new UseCaseValidationException("Lancamento nao encontrado.");

            if (lancamento.EmpresaId != cmd.EmpresaId)
                throw new UseCaseValidationException("Lancamento nao pertence a empresa.");

            LancamentoBaixa baixa;
            try
            {
                baixa = lancamento.RegistrarBaixa(
                    valor: cmd.Valor,
                    dataBaixa: cmd.DataBaixa == default ? DateTime.UtcNow : cmd.DataBaixa,
                    meioPagamento: cmd.MeioPagamento,
                    contaBancariaId: cmd.ContaBancariaId,
                    chaveExterna: cmd.ChaveExterna,
                    observacao: cmd.Observacao,
                    registradoPorUserId: cmd.RegistradoPorUserId,
                    registradoPorNome: cmd.RegistradoPorNome);
            }
            catch (RegraDeDominioVioladaException ex)
            {
                throw new UseCaseValidationException(ex.Message);
            }

            await repo.UpdateAsync(lancamento, token);
            await uow.CommitAsync();

            var eventos = lancamento.EventosPendentes.ToArray();
            lancamento.LimparEventosPendentes();

            if (publicadorEventos is not null)
            {
                foreach (var evt in eventos)
                    await publicadorEventos.PublicarAsync(evt);
            }

            logger.LogInformation(
                "Baixa {BaixaId} de {Valor:F2} aplicada ao lancamento {LancamentoId}; status={Status}, restante={Restante:F2}.",
                baixa.Id, baixa.Valor, lancamento.Id, lancamento.Status, lancamento.ValorRestante);

            return new BaixarLancamentoResult(
                LancamentoId: lancamento.Id,
                BaixaId: baixa.Id,
                ValorBaixado: baixa.Valor,
                ValorRestante: lancamento.ValorRestante,
                StatusResultante: lancamento.Status);
        }, ct);
    }
}
