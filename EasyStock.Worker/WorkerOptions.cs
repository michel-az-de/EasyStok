namespace EasyStock.Worker;

/// <summary>
/// Opções específicas do Worker. Props relacionadas ao pipeline de notificações
/// (DispatcherBatchSize, ShardCount, intervalos, RetencaoLogs, AnonimizarHora) foram
/// migradas para <see cref="EasyStock.Application.Services.Notifications.NotificationsHostingOptions"/>
/// (seção <c>Notifications:Hosting</c>). Retro-compat de leitura mantida via PostConfigure
/// em <c>AddNotificationsCore</c>.
/// </summary>
public sealed class WorkerOptions
{
    public const string Section = "Worker";

    public bool NoMigrate { get; set; }

    /// <summary>Intervalo (em segundos) entre ticks do SlaMonitorService. Mínimo 60. Padrão 300 (5 min).</summary>
    public int SlaMonitorIntervalSeconds { get; set; } = 300;
}
