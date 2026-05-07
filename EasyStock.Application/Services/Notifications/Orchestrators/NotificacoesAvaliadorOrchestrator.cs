using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Implementação pura — processa eventos pendentes via <see cref="INotificadorService"/> e
/// detecta rotinas Cron disparáveis. Sem loop, sem sleep — invocada por wrapper hosted ou trigger HTTP.
/// </summary>
public sealed class NotificacoesAvaliadorOrchestrator(
    INotificadorService notificadorService,
    IEventoNotificacaoRepository eventoRepo,
    IRotinaRepository rotinaRepo,
    RotinaScheduler rotinaScheduler,
    ILogger<NotificacoesAvaliadorOrchestrator> logger) : INotificacoesAvaliadorOrchestrator
{
    public async Task ExecutarRodadaAsync(TimeSpan janelaAvaliacao, CancellationToken ct = default)
    {
        // 1. Processa eventos pendentes (criados por coletores de estado / publicação direta)
        var pendentes = await eventoRepo.ListarPendentesAsync(limit: 200, ct);
        foreach (var evento in pendentes)
        {
            try
            {
                await notificadorService.AvaliarEventoAsync(evento, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao avaliar evento {EventoId}", evento.Id);
            }
        }

        if (pendentes.Count > 0)
            logger.LogInformation("AvaliadorOrchestrator: processados {Count} eventos pendentes.", pendentes.Count);

        // 2. Detecta rotinas Cron disparáveis (eventos serão criados pelos coletores de estado)
        var agora = DateTime.UtcNow;
        var ultimaExecucao = agora - (janelaAvaliacao > TimeSpan.Zero ? janelaAvaliacao : TimeSpan.FromMinutes(2));
        var rotinasAtivas = await rotinaRepo.ListarAtivasAsync(ct: ct);

        foreach (var rotina in rotinasAtivas.Where(r => r.TriggerTipo == TriggerTipoRotina.Cron))
        {
            if (!rotinaScheduler.DeveriasExecutar(rotina, ultimaExecucao, agora))
                continue;

            logger.LogInformation("Rotina cron {Codigo} disparada.", rotina.Codigo);
            // Coletores de estado criam os eventos correspondentes — apenas logamos aqui.
        }
    }
}
