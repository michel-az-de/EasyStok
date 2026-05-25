namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Contrato do dispatcher do Worker. Lê lotes do outbox e envia via ICanalNotificacao.
/// </summary>
public interface INotificationDispatcher
{
    Task<int> ProcessarBatchAsync(int shardKey, int batchSize = 50, CancellationToken ct = default);
}
