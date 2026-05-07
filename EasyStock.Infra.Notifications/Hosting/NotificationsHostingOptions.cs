namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Modos de hosting do pipeline de notificações.
/// </summary>
public enum NotificationsHostingMode
{
    /// <summary>Loops in-process (BackgroundServices) registrados.</summary>
    Hosted,
    /// <summary>Sem loops — apenas orchestrators registrados.
    /// Útil para modo cron-only (endpoints HTTP) ou quando outra instância hospeda.</summary>
    Disabled
}

/// <summary>
/// Mecanismo de wakeup do dispatcher quando em modo <see cref="NotificationsHostingMode.Hosted"/>.
/// </summary>
public enum OutboxSignalerKind
{
    /// <summary>LISTEN/NOTIFY do PostgreSQL — latência &lt;1s.</summary>
    Postgres,
    /// <summary>PeriodicTimer simples — sem wakeup imediato.</summary>
    Polling
}

/// <summary>
/// Opções unificadas do hosting de notificações. Lê seção "Notifications:Hosting".
/// Substitui WorkerOptions (que mantém retro-compat por 1 release).
/// </summary>
public sealed class NotificationsHostingOptions
{
    public const string Section = "Notifications:Hosting";

    public NotificationsHostingMode Mode { get; set; } = NotificationsHostingMode.Hosted;
    public OutboxSignalerKind Signaler { get; set; } = OutboxSignalerKind.Postgres;

    public int ShardCount { get; set; } = 4;
    public int DispatcherBatchSize { get; set; } = 50;
    public int DispatcherPollingIntervalMs { get; set; } = 10_000;

    public int AvaliadorIntervalSeconds { get; set; } = 60;
    public int ColetorIntervalSeconds { get; set; } = 300;
}
