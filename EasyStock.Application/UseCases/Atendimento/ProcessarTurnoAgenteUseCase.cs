using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>
/// Drena um <see cref="ProcessarTurnoAgenteJob"/> da fila <see cref="FilaAtendimentoNomes.TurnoAgente"/>
/// (enfileirado pelo webhook, S03/S05) e roda o turno do <see cref="AgenteAtendimentoService"/> (S06).
/// Fora de requisição não há claim JWT: o tenant é definido pelo job antes de qualquer consulta, senão
/// o filtro global e a RLS zeram as linhas.
/// </summary>
public sealed class ProcessarTurnoAgenteUseCase(
    AgenteAtendimentoService agente,
    ITenantContextAccessor tenantContext,
    ILogger<ProcessarTurnoAgenteUseCase> logger)
{
    public async Task<ResultadoTurnoAgente> ExecuteAsync(ProcessarTurnoAgenteJob job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        tenantContext.SetCurrentTenant(job.EmpresaId);

        try
        {
            return await agente.ProcessarTurnoAsync(job.EmpresaId, job.ConversaId, DateTime.UtcNow, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Turno do agente falhou na conversa {ConversaId}.", job.ConversaId);
            return ResultadoTurnoAgente.Ignorado;
        }
    }
}
