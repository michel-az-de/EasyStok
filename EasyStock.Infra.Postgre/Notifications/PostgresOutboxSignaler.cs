using EasyStock.Application.Ports.Output;
using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EasyStock.Infra.Postgre.Notifications;

/// <summary>
/// Signaler concreto Postgres — escuta canal 'notif_outbox' via LISTEN/NOTIFY e libera
/// o semaphore interno para wakeup imediato do dispatcher (latência &lt;1s).
/// Fallback automático a polling de 10s para resiliência contra reconexão.
/// Roda como <see cref="IHostedService"/> para gerenciar a conexão dedicada.
/// </summary>
public sealed class PostgresOutboxSignaler : IOutboxSignaler, IHostedService, IAsyncDisposable
{
    // maxCount=1: qualquer nº de NOTIFYs = 1 wakeup pendente (anti-flood)
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostgresOutboxSignaler> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenLoop;
    private volatile bool _disposed;

    // Throttle de heartbeat — grava no máximo 1x/min (signaler nao tem "tick" tradicional;
    // serve como liveness do LISTEN persistente).
    private DateTime _ultimoHeartbeat = DateTime.MinValue;
    private static readonly TimeSpan HeartbeatThrottle = TimeSpan.FromSeconds(60);

    private const int FallbackPollingMs = 10_000;

    public PostgresOutboxSignaler(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<PostgresOutboxSignaler> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public Task WaitAsync(CancellationToken ct) => _signal.WaitAsync(ct);

    public void Signal()
    {
        // Race-safe: callback do NpgsqlConnection.Notification pode disparar
        // durante DisposeAsync. Checa flag antes de tocar no semáforo.
        if (_disposed) return;
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* já há wakeup pendente */ }
        catch (ObjectDisposedException) { /* race entre check e release — ignora */ }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listenLoop = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null) _cts.Cancel();
        if (_listenLoop is not null)
        {
            try { await _listenLoop; }
            catch (OperationCanceledException) { /* esperado */ }
        }
    }

    private async Task ListenLoopAsync(CancellationToken stoppingToken)
    {
        var connStr = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            _logger.LogWarning("PostgresOutboxSignaler: ConnectionString não configurada — usando apenas polling fallback.");
            await RegistrarHeartbeatAsync("Skip",
                "ConnectionString ausente — modo polling fallback", stoppingToken);
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
                    _logger.LogDebug("NOTIFY recebido: canal={Canal} payload={Payload}",
                        args.Channel, args.Payload);
                    Signal();
                };

                await conn.OpenAsync(stoppingToken);
                await using (var cmd = new NpgsqlCommand("LISTEN notif_outbox", conn))
                    await cmd.ExecuteNonQueryAsync(stoppingToken);

                _logger.LogInformation("PostgresOutboxSignaler: LISTEN ativo em 'notif_outbox'.");
                await RegistrarHeartbeatAsync("OK", "LISTEN ativo em 'notif_outbox'", stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(TimeSpan.FromMilliseconds(FallbackPollingMs), stoppingToken);
                    Signal(); // ao retornar do timeout, sinaliza wakeup periódico (fallback)
                    await RegistrarHeartbeatAsync("OK", "LISTEN ativo", stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PostgresOutboxSignaler: erro na conexão LISTEN — reconectando em 5s.");
                await RegistrarHeartbeatAsync("Erro",
                    ex.GetType().Name + ": " + ex.Message, stoppingToken);
                try { await Task.Delay(5_000, stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task FallbackPollingLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Signal();
            await RegistrarHeartbeatAsync("OK", "polling fallback (sem LISTEN)", ct);
            try { await Task.Delay(FallbackPollingMs, ct); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Grava heartbeat respeitando throttle de <see cref="HeartbeatThrottle"/>.
    /// Erros sao sempre gravados (sem throttle) pra alertar imediato.
    /// </summary>
    private async Task RegistrarHeartbeatAsync(string status, string? detalhe, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (status == "OK" && now - _ultimoHeartbeat < HeartbeatThrottle) return;
        _ultimoHeartbeat = now;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IHeartbeatRecorder>();
            await recorder.RecordAsync("OutboxSignaler", status, detalhe, null, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao gravar heartbeat do OutboxSignaler");
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Marca disposed ANTES de cancelar/disposed objetos — Signal() vai virar no-op
        // mesmo se o callback do Npgsql disparar entre o cancel e o dispose do semáforo.
        _disposed = true;

        if (_cts is not null) _cts.Cancel();
        if (_listenLoop is not null)
        {
            try { await _listenLoop; } catch { /* shutdown */ }
        }
        _cts?.Dispose();
        _signal.Dispose();
    }
}
