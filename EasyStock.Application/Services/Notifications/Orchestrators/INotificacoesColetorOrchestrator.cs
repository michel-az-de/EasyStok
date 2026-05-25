namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Orquestra 1 rodada de coleta de eventos de estado: itera todos os
/// <see cref="EasyStock.Application.Ports.Output.Notifications.IColetorEventoNotificacao"/>
/// registrados no DI e os executa sequencialmente.
/// Idempotente (cada coletor já garante deduplicação via correlationId).
/// </summary>
public interface INotificacoesColetorOrchestrator
{
    Task ExecutarRodadaAsync(CancellationToken ct = default);
}
