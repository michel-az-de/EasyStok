using Npgsql;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Escuta LISTEN/NOTIFY do PostgreSQL no canal 'notif_outbox'.
/// Quando o trigger de INSERT dispara pg_notify, acorda o dispatcher imediatamente.
/// Fallback automático a polling de 10s para resiliência contra reconexão.
/// </summary>
public sealed class OutboxListenService(
    IConfiguration configuration,
    ILogger<OutboxListenService> logger) : BackgroundService
{
    // Sinaliza ao dispatcher que há mensagens novas (maxCount=1: qualquer nº de NOTIFYs = 1 wakeup pendente)
    internal static readonly SemaphoreSlim NotifySignal = new(0, 1);

    private const int FallbackPollingMs = 10_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connStr = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            logger.LogWarning("OutboxListenService: ConnectionString não configurada — usando apenas polling.");
            await FallbackPollingLoopAsync(stoppingToken);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connStr);
                conn.Notification += (_, args) =>
                {
                    logger.LogDebug("NOTIFY recebido: canal={Canal} payload={Payload}",
                        args.Channel, args.Payload);
                    try { NotifySignal.Release(); } catch (SemaphoreFullException) { }
                };

                await conn.OpenAsync(stoppingToken);
                await using (var cmd = new NpgsqlCommand("LISTEN notif_outbox", conn))
                    await cmd.ExecuteNonQueryAsync(stoppingToken);

                logger.LogInformation("OutboxListenService: LISTEN ativo em 'notif_outbox'.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    // Aguarda notificação com timeout (fallback a polling)
                    await conn.WaitAsync(TimeSpan.FromMilliseconds(FallbackPollingMs), stoppingToken);
                    try { NotifySignal.Release(); } catch (SemaphoreFullException) { }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OutboxListenService: erro na conexão LISTEN — reconectando em 5s.");
                await Task.Delay(5_000, stoppingToken);
            }
        }
    }

    private async Task FallbackPollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { NotifySignal.Release(); } catch (SemaphoreFullException) { }
            await Task.Delay(FallbackPollingMs, ct);
        }
    }
}
