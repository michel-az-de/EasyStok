namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Sinal de wakeup do dispatcher do outbox. Implementação concreta decide o mecanismo:
/// <see cref="EasyStock.Infra.Postgre.Notifications.PostgresOutboxSignaler"/> usa LISTEN/NOTIFY
/// (latência &lt;1s); <see cref="PollingOutboxSignaler"/> apenas dispara em intervalos fixos
/// (fallback para ambientes sem PG dedicado ou sem permissão de LISTEN).
/// </summary>
public interface IOutboxSignaler
{
    /// <summary>Aguarda próximo sinal ou timeout interno do impl.</summary>
    Task WaitAsync(CancellationToken ct);

    /// <summary>Sinaliza wakeup imediato (idempotente — múltiplas chamadas = 1 wakeup pendente).</summary>
    void Signal();
}
