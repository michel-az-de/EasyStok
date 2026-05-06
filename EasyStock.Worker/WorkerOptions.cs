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
}
