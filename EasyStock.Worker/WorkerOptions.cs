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

    /// <summary>Intervalo (em segundos) entre ticks do AgendamentoNotificacaoService. Mínimo 60. Padrão 60 (1 min, pra acertar a janela de 10min).</summary>
    public int AgendamentoNotificacaoIntervalSeconds { get; set; } = 60;

    /// <summary>Intervalo (em segundos) entre ticks do EndpointHealthMonitorService. Mínimo 60. Padrão 300 (5 min).</summary>
    public int EndpointHealthIntervalSeconds { get; set; } = 300;

    /// <summary>Intervalo (em segundos) entre ticks do ReprocessarContingenciaBackgroundService. Mínimo 30. Padrão 60 (1 min).</summary>
    public int NfeContingenciaIntervalSeconds { get; set; } = 60;

    /// <summary>Tamanho do batch de NFC-e em FalhaTransiente processado por tick. Default 50, max 500.</summary>
    public int NfeContingenciaBatchSize { get; set; } = 50;

    /// <summary>Intervalo (em segundos) entre ticks do RenovacaoCertificadoA1BackgroundService. Mínimo 3600 (1h). Padrão 21600 (6h).</summary>
    public int NfeRenovacaoCertIntervalSeconds { get; set; } = 21600;
}
