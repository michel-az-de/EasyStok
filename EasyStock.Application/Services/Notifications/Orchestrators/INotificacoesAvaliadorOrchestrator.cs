namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Orquestra 1 rodada de avaliação:
/// (a) processa <see cref="EasyStock.Domain.Entities.Notifications.EventoNotificacao"/> pendentes,
/// (b) detecta rotinas Cron que deveriam ter disparado.
/// Idempotente — pode ser invocado por loop in-process ou trigger HTTP.
/// </summary>
public interface INotificacoesAvaliadorOrchestrator
{
    /// <param name="janelaAvaliacao">Janela usada como "última execução" para o RotinaScheduler.
    /// Tipicamente 2× o intervalo do loop. Default 2 min.</param>
    Task ExecutarRodadaAsync(TimeSpan janelaAvaliacao, CancellationToken ct = default);
}
