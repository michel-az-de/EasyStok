namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Varredores de estado que geram EventoNotificacao para rotinas de batch/cron.
/// Cada implementação cobre um domínio (produtos vencendo, tarefas, assinaturas, etc.).
/// </summary>
public interface IColetorEventoNotificacao
{
    Task ColetarAsync(CancellationToken ct = default);
}
