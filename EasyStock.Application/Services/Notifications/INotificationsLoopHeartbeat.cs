namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Heartbeat dos BackgroundServices do pipeline de notificacoes (Dispatcher,
/// Avaliador, Coletor). Cada loop bate ao concluir uma iteracao (sucesso OU
/// erro logado); fica stale quando o loop pendura (deadlock, exception fatal,
/// thread bloqueada). Health check le isso pra detectar dispatcher travado em
/// hosts que rodam o pipeline in-process (API com Mode=Hosted).
/// Singleton — vive entre Hosted e API.
/// </summary>
public interface INotificationsLoopHeartbeat
{
    void Heartbeat(string loopName);
    DateTimeOffset? LastBeat(string loopName);
    IReadOnlyDictionary<string, DateTimeOffset> Snapshot();
}

public static class NotificationsLoops
{
    public const string Dispatcher = "dispatcher";
    public const string Avaliador = "avaliador";
    public const string Coletor = "coletor";
}
