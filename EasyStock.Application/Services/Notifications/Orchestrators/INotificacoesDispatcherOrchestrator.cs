namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Orquestra 1 rodada completa de despacho do outbox (N1): reserva as mensagens elegíveis de todas as empresas
/// (<c>FOR UPDATE SKIP LOCKED</c>, com lease) e processa cada uma em escopo próprio, com o tenant da empresa e
/// <c>try/catch</c> próprio. Idempotente entre instâncias: o claim e o lease impedem dupla entrega, sem advisory lock.
/// </summary>
public interface INotificacoesDispatcherOrchestrator
{
    /// <param name="shardCount">Ignorado: <c>ShardKey</c> não participa mais do claim (N1). Mantido por compatibilidade.</param>
    /// <returns>Total de mensagens processadas na rodada.</returns>
    Task<int> ExecutarRodadaAsync(int shardCount, int batchSize, CancellationToken ct = default);
}
