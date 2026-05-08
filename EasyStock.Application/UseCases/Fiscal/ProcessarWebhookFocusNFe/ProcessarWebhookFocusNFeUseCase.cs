using EasyStock.Application.Ports.Output.Integration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;

/// <summary>
/// Processa webhook do Focus avisando mudança de status de uma nota.
/// Idempotente — se a nota já está no status alvo, retorna no-op.
/// Webhook duplicado é tratado como replay seguro.
/// </summary>
public sealed class ProcessarWebhookFocusNFeUseCase(
    INotaFiscalRepository repo,
    IPublicadorEventoIntegracao eventos,
    IUnitOfWork uow,
    ILogger<ProcessarWebhookFocusNFeUseCase> log)
    : IUseCase<ProcessarWebhookFocusNFeCommand, ProcessarWebhookFocusNFeResult>
{
    public async Task<ProcessarWebhookFocusNFeResult> ExecuteAsync(ProcessarWebhookFocusNFeCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.ChaveAcesso))
            return new ProcessarWebhookFocusNFeResult(false);

        var ct = CancellationToken.None;
        NotaFiscal? nota;
        try
        {
            nota = await repo.ObterPorChaveAsync(cmd.ChaveAcesso, ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Webhook chave invalida {Chave}", cmd.ChaveAcesso);
            return new ProcessarWebhookFocusNFeResult(false);
        }

        if (nota is null)
        {
            log.LogInformation("Webhook chave {Chave} sem nota correspondente — ignorando.", cmd.ChaveAcesso);
            return new ProcessarWebhookFocusNFeResult(false);
        }

        var statusNovo = MapearStatus(cmd.Status);
        if (statusNovo is null)
        {
            log.LogWarning("Webhook status desconhecido '{Status}' para chave {Chave}", cmd.Status, cmd.ChaveAcesso);
            return new ProcessarWebhookFocusNFeResult(false);
        }

        if (nota.Status == statusNovo)
            return new ProcessarWebhookFocusNFeResult(true);

        if (!NotaFiscalStateMachine.TransicaoValida(nota.Status, statusNovo.Value))
        {
            log.LogWarning("Webhook transicao invalida {De}→{Para} chave {Chave} — ignorando.",
                nota.Status, statusNovo, cmd.ChaveAcesso);
            return new ProcessarWebhookFocusNFeResult(false);
        }

        await uow.ExecuteInTransactionAsync(async token =>
        {
            switch (statusNovo)
            {
                case StatusNotaFiscal.Autorizada:
                    if (nota.Status == StatusNotaFiscal.EmContingencia)
                        nota.MarcarAutorizadaPosContingencia(
                            cmd.Protocolo ?? "0", cmd.XmlEvento ?? "<auth/>",
                            cmd.DhEvento ?? DateTime.UtcNow);
                    else
                        nota.MarcarAutorizada(
                            cmd.Protocolo ?? "0", cmd.XmlEvento ?? "<auth/>",
                            cmd.DhEvento ?? DateTime.UtcNow);
                    break;
                case StatusNotaFiscal.Cancelada:
                    nota.MarcarCancelada(
                        cmd.Protocolo ?? "0", cmd.XmlEvento ?? "<canc/>",
                        cmd.DhEvento ?? DateTime.UtcNow);
                    break;
                case StatusNotaFiscal.Denegada:
                    nota.MarcarDenegada(cmd.Codigo ?? "DEN", cmd.Motivo ?? "Denegada");
                    break;
                case StatusNotaFiscal.Rejeitada:
                    nota.MarcarRejeitada(cmd.Codigo ?? "REJ", cmd.Motivo ?? "Rejeitada");
                    break;
            }

            await repo.AtualizarAsync(nota, token);
            await eventos.PublicarAsync(
                empresaId: nota.EmpresaId,
                tipoEvento: $"nfce.{statusNovo.Value.ToString().ToLowerInvariant()}",
                aggregateType: nameof(NotaFiscal),
                aggregateId: nota.Id,
                payload: new
                {
                    notaFiscalId = nota.Id,
                    chaveAcesso = nota.ChaveAcesso.Valor,
                    statusNovo = statusNovo.Value.ToString(),
                    correlationId = cmd.CorrelationId,
                },
                ct: token);
            await uow.CommitAsync();
        });

        return new ProcessarWebhookFocusNFeResult(true);
    }

    private static StatusNotaFiscal? MapearStatus(string status) =>
        (status ?? "").Trim().ToLowerInvariant() switch
        {
            "autorizado" => StatusNotaFiscal.Autorizada,
            "cancelado" => StatusNotaFiscal.Cancelada,
            "denegado" => StatusNotaFiscal.Denegada,
            "rejeitado" => StatusNotaFiscal.Rejeitada,
            _ => null,
        };
}
