using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>
/// Drena um <see cref="ProcessarTurnoAgenteJob"/> da fila <see cref="FilaAtendimentoNomes.TurnoAgente"/>
/// (enfileirado pelo webhook, S03/S05) e roda o turno do <see cref="AgenteAtendimentoService"/> (S06).
/// Fora de requisição não há claim JWT: o tenant é definido pelo job antes de qualquer consulta, senão
/// o filtro global e a RLS zeram as linhas.
///
/// <para>
/// #2133: turno que lança exceção devolve a conversa à equipe pelo <see cref="IEscaladorConversa"/> (sai do
/// automático, aparece como "passou para você", nota interna do sistema e aviso da ADR-0057). Só logar
/// deixava a conversa no automático, sem resposta e sem ninguém avisado.
/// </para>
/// </summary>
public sealed class ProcessarTurnoAgenteUseCase(
    AgenteAtendimentoService agente,
    ITenantContextAccessor tenantContext,
    IConversaRepository conversaRepository,
    IEscaladorConversa escalador,
    IUnitOfWork unitOfWork,
    ILogger<ProcessarTurnoAgenteUseCase> logger)
{
    public const string MotivoFalhaDoTurno = "o Assistente falhou ao responder; leia a mensagem do cliente e responda você";

    public async Task<ResultadoTurnoAgente> ExecuteAsync(ProcessarTurnoAgenteJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        tenantContext.SetCurrentTenant(job.EmpresaId);

        try
        {
            return await agente.ProcessarTurnoAsync(job.EmpresaId, job.ConversaId, DateTime.UtcNow, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Turno do agente falhou na conversa {ConversaId}.", job.ConversaId);
            return await DevolverParaEquipeAsync(job, ct);
        }
    }

    /// <summary>
    /// Melhor esforço: a falha do turno já está no log e o worker não pode cair por causa da escalada. O que o
    /// turno deixou rastreado e sem gravar é descartado antes (pode ser o motivo da falha), e a conversa é
    /// relida para escalar a partir do estado do banco. Quem já assumiu ou encerrou a conversa não é tocado.
    /// </summary>
    private async Task<ResultadoTurnoAgente> DevolverParaEquipeAsync(ProcessarTurnoAgenteJob job, CancellationToken ct)
    {
        try
        {
            unitOfWork.DescartarAlteracoesPendentes();
            var conversa = await conversaRepository.ObterPorIdAsync(job.EmpresaId, job.ConversaId, ct);
            if (conversa is not { Situacao: SituacaoConversa.Automatica }) return ResultadoTurnoAgente.Ignorado;

            await escalador.EscalarAsync(job.EmpresaId, conversa, MotivoFalhaDoTurno, DateTime.UtcNow, ct);
            await unitOfWork.CommitAsync();
            return new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Turno do agente: não foi possível devolver a conversa {ConversaId} para a equipe.", job.ConversaId);
            return ResultadoTurnoAgente.Ignorado;
        }
    }
}
