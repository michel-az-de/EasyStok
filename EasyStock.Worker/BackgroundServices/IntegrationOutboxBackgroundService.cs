using System.Diagnostics;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// BackgroundService que consome o outbox de eventos de integração externa.
/// Cria scope por tick (dispatcher é Scoped), executa rodada, dorme e
/// repete. Sem advisory lock nesta versão — single-instance é seguro
/// porque o outbox é unique-key por IdempotencyKey e cada item é
/// pegado em transação. Em multi-réplica pode haver concorrência leve
/// (mesmo evento pego 2x); idempotência do handler resolve.
///
/// <para>
/// Quando volume crescer (>10k pendentes simultâneos), trocar pra padrão
/// shard + advisory lock como o de notificações
/// (<c>DispatcherLoopHostedService</c>).
/// </para>
///
/// <para>
/// <b>Configuração</b> (appsettings.json):
/// <code>
/// "Integration": {
///   "Outbox": {
///     "Enabled": true,
///     "PollingIntervalSeconds": 30,
///     "BatchSize": 50
///   }
/// }
/// </code>
/// </para>
/// </summary>
public sealed class IntegrationOutboxBackgroundService(
    IServiceProvider serviceProvider,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    ILogger<IntegrationOutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration.GetValue("Integration:Outbox:Enabled", defaultValue: true);
        if (!enabled)
        {
            logger.LogInformation("IntegrationOutboxBackgroundService desabilitado via Integration:Outbox:Enabled=false.");
            return;
        }

        var pollingInterval = TimeSpan.FromSeconds(
            configuration.GetValue("Integration:Outbox:PollingIntervalSeconds", defaultValue: 30));
        var batchSize = configuration.GetValue("Integration:Outbox:BatchSize", defaultValue: 50);

        logger.LogInformation(
            "IntegrationOutboxBackgroundService iniciado — polling={Interval} batch={Batch}.",
            pollingInterval, batchSize);

        // Tick inicial breve pra dar tempo do app subir antes do primeiro hit.
        try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            int processados = 0;
            string status = "OK";
            string? detalhe = null;
            bool cancelado = false;

            try
            {
                processados = await RodarRodadaAsync(batchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                cancelado = true;
            }
            catch (Exception ex)
            {
                status = "Erro";
                detalhe = ex.GetType().Name + ": " + ex.Message;
                logger.LogError(ex,
                    "IntegrationOutboxBackgroundService: erro na rodada — continuando próximo tick.");
            }
            finally
            {
                sw.Stop();
                await GravarHeartbeatAsync("IntegrationOutbox", status, detalhe,
                    processados, (int)sw.ElapsedMilliseconds, stoppingToken);
            }

            if (cancelado) break;

            // Se processou batch cheio, há mais — rodar imediatamente sem esperar.
            if (status == "OK" && processados >= batchSize) continue;

            try { await Task.Delay(pollingInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("IntegrationOutboxBackgroundService finalizado.");
    }

    private async Task<int> RodarRodadaAsync(int batchSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IIntegrationEventDispatcher>();
        return await dispatcher.ExecutarRodadaAsync(batchSize, ct);
    }

    private async Task GravarHeartbeatAsync(
        string servico, string status, string? detalhe,
        int? itensProcessados, int? duracaoMs, CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<IHeartbeatRecorder>();
            await recorder.RecordAsync(servico, status, detalhe, itensProcessados, duracaoMs, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao gravar heartbeat do IntegrationOutbox");
        }
    }
}
