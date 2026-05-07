namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Orquestra 1 rodada completa de despacho — processa todos os shards do outbox.
/// Granularidade complementar ao <see cref="EasyStock.Application.Ports.Output.Notifications.INotificationDispatcher"/>
/// (que processa 1 shard por chamada — usado pelo endpoint HTTP cron com ?shard=N).
/// Idempotente — advisory lock impede dupla entrega entre instâncias.
/// </summary>
public interface INotificacoesDispatcherOrchestrator
{
    /// <returns>Total de mensagens processadas (somatório de todos os shards).</returns>
    Task<int> ExecutarRodadaAsync(int shardCount, int batchSize, CancellationToken ct = default);
}
