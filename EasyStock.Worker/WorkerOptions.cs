namespace EasyStock.Worker;

public sealed class WorkerOptions
{
    public const string Section = "Worker";

    public int DispatcherBatchSize { get; set; } = 50;
    public int DispatcherPollingIntervalMs { get; set; } = 10_000;
    public int ShardCount { get; set; } = 4;
    public int AvaliadoresIntervalSeconds { get; set; } = 60;
    public int ColetorIntervalSeconds { get; set; } = 300;
    public bool NoMigrate { get; set; }

    /// <summary>Dias de retenção de destinatário/corpo nos logs de envio. Padrão 90d.</summary>
    public int RetencaoLogsDias { get; set; } = 90;

    /// <summary>Hora UTC em que o serviço de anonimização roda diariamente. Padrão 3h.</summary>
    public int AnonimizarHoraUtc { get; set; } = 3;
}
