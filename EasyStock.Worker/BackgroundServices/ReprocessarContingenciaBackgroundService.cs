using EasyStock.Application.UseCases.Fiscal.ReprocessarContingencia;
using EasyStock.Infra.Postgre.Concurrency;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Worker que reprocessa NFC-e em <see cref="EasyStock.Domain.Fiscal.StatusNfe.FalhaTransiente"/>.
/// A cada <see cref="WorkerOptions.NfeContingenciaIntervalSeconds"/> (default 60s),
/// executa <see cref="ReprocessarContingenciaUseCase"/> que bypassa RLS e itera
/// cross-tenant. Single-instance via advisory lock para evitar duplicacao em multi-replica.
/// </summary>
public sealed class ReprocessarContingenciaBackgroundService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<ReprocessarContingenciaBackgroundService> logger) : BackgroundService
{
    // 0x4E464345 = "NFCE" (NFCe contingencia) — lock unico para single-instance
    private const long LockId = 0x4E46_4345_0000_0001L;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ReprocessarContingenciaBackgroundService iniciado");

        var intervaloSegundos = Math.Max(30, options.Value.NfeContingenciaIntervalSeconds);
        var batchSize = Math.Clamp(options.Value.NfeContingenciaBatchSize, 1, 500);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutarTickAsync(batchSize, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro durante tick do ReprocessarContingenciaBackgroundService");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(intervaloSegundos), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ExecutarTickAsync(int batchSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>();

        await advisoryLock.TentarExecutarAsync(LockId, async _ =>
        {
            var useCase = sp.GetRequiredService<ReprocessarContingenciaUseCase>();
            var result = await useCase.ExecuteAsync(new ReprocessarContingenciaCommand(batchSize));

            if (result.Processadas > 0)
            {
                logger.LogInformation(
                    "Contingencia tick: processadas={Processadas} autorizadas={Auth} rejeitadas={Rej} transientes={Tra}",
                    result.Processadas, result.Autorizadas, result.Rejeitadas, result.AindaTransientes);
            }
        }, ct);
    }
}
