using EasyStock.Application.Ports.Output.Security;
using EasyStock.Domain.Fiscal;

namespace EasyStock.Application.UseCases.Fiscal.ProcessarWebhookFocusNFe;

/// <summary>
/// Processa webhook do Focus NFe (status change). Aplica fix B-054: usa
/// <see cref="IRowLevelSecurityBypass.Begin"/> porque o webhook chega sem JWT —
/// busca por <see cref="EasyStock.Domain.Fiscal.NfeDocumento.ChaveAcesso"/>
/// que nao traz tenant. Sem bypass, o Global Query Filter elimina o resultado
/// e a nota fica presa em <see cref="StatusNfe.EnviadaAguardandoRetorno"/>.
///
/// <para>
/// <b>Idempotencia:</b> webhook pode chegar 2x (retry do Focus). Se o status
/// ja foi aplicado, MarcarAutorizada/MarcarRejeitada/Cancelar fazem early-return
/// (logica idempotente no agregado).
/// </para>
/// </summary>
public class ProcessarWebhookFocusNFeUseCase(
    INfeRepository nfeRepo,
    IRowLevelSecurityBypass rlsBypass,
    IUnitOfWork uow,
    ILogger<ProcessarWebhookFocusNFeUseCase> logger) : IUseCase<ProcessarWebhookFocusNFeCommand, ProcessarWebhookFocusNFeResult>
{
    public async Task<ProcessarWebhookFocusNFeResult> ExecuteAsync(ProcessarWebhookFocusNFeCommand cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.ChaveAcesso) || cmd.ChaveAcesso.Length != 44)
            throw new UseCaseValidationException("ChaveAcesso de 44 digitos obrigatoria.");
        if (string.IsNullOrWhiteSpace(cmd.StatusGateway))
            throw new UseCaseValidationException("StatusGateway obrigatorio.");

        using var _ = rlsBypass.Begin();

        var nfe = await nfeRepo.FindByChaveAcessoAsync(cmd.ChaveAcesso);
        if (nfe is null)
        {
            logger.LogWarning(
                "Webhook ignorado: chave {Chave} nao encontrada em NfeDocumento.", cmd.ChaveAcesso);
            return new ProcessarWebhookFocusNFeResult(Aplicado: false, NfeId: null, StatusFinal: null);
        }

        var status = NormalizarStatus(cmd.StatusGateway);

        await uow.ExecuteInTransactionAsync(async _ =>
        {
            // Recarrega DENTRO da transacao para evitar stale (xmin conflict)
            var nfeTx = await nfeRepo.FindByChaveAcessoAsync(cmd.ChaveAcesso)
                ?? throw new InvalidOperationException($"NfeDocumento {nfe.Id} sumiu antes do commit webhook.");

            switch (status)
            {
                case "autorizado":
                case "autorizada":
                    if (string.IsNullOrWhiteSpace(cmd.ProtocoloAutorizacao))
                        throw new UseCaseValidationException("ProtocoloAutorizacao obrigatorio para status autorizado.");
                    nfeTx.MarcarAutorizada(
                        chaveAcesso: cmd.ChaveAcesso,
                        protocoloAutorizacao: cmd.ProtocoloAutorizacao,
                        xmlAssinadoStorageKey: cmd.XmlAssinadoUrl,
                        danfeUrl: cmd.DanfeUrl,
                        origem: "webhook-focus");
                    break;

                case "rejeitado":
                case "rejeitada":
                    nfeTx.MarcarRejeitada(
                        motivo: cmd.MotivoRejeicao ?? "Rejeitado (motivo nao informado pelo webhook).",
                        origem: "webhook-focus");
                    break;

                case "cancelado":
                case "cancelada":
                    nfeTx.Cancelar(
                        motivo: cmd.MotivoRejeicao ?? "Cancelamento confirmado via webhook.",
                        origem: "webhook-focus");
                    break;

                case "erro":
                case "erro_autorizacao":
                    nfeTx.MarcarFalhaTransiente(detalhe: cmd.MotivoRejeicao, origem: "webhook-focus");
                    break;

                default:
                    logger.LogWarning(
                        "Webhook status desconhecido: {Status} para chave {Chave}. Ignorado.",
                        cmd.StatusGateway, cmd.ChaveAcesso);
                    return;
            }

            await nfeRepo.UpdateAsync(nfeTx);
        });

        var nfeFinal = await nfeRepo.FindByChaveAcessoAsync(cmd.ChaveAcesso);
        logger.LogInformation(
            "Webhook aplicado: chave {Chave} -> status {Status}.",
            cmd.ChaveAcesso, nfeFinal?.Status);

        return new ProcessarWebhookFocusNFeResult(
            Aplicado: true,
            NfeId: nfe.Id,
            StatusFinal: nfeFinal?.Status.ToString());
    }

    private static string NormalizarStatus(string s) =>
        s.Trim().ToLowerInvariant();
}
