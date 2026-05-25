using EasyStock.Application.UseCases.Storefront.Webhook;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Hosting;

/// <summary>
/// Background service que processa webhooks MercadoPago em status
/// <see cref="WebhookProcessadoStatus.Received"/> (ADR-0006 §Process).
///
/// <para>
/// Pattern: polling a cada ~10 s, batch de até 50 webhooks por iteração.
/// Cada webhook chama <see cref="ProcessarWebhookMpUseCase"/> que faz
/// <c>GetPayment</c> contra MP como fonte da verdade.
/// </para>
///
/// <para>
/// Jitter ±2 s no intervalo evita sincronização entre múltiplas instâncias
/// quando deploy horizontal (Kubernetes).
/// </para>
///
/// <para>
/// <strong>Por que vive em Infra.Postgre/Hosting</strong>: precisa de
/// <c>EasyStockDbContext</c> para query direta ao backlog ativo (índice
/// filtrado <c>ix_webhook_processado_received_recebido_em</c>) e de
/// <see cref="BackgroundService"/> de <c>Microsoft.Extensions.Hosting</c> que
/// não é referenciado por <c>EasyStock.Application</c>.
/// </para>
/// </summary>
public sealed class ProcessarWebhookMpPendenteBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessarWebhookMpPendenteBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan IntervaloBase = TimeSpan.FromSeconds(10);
    private const int MaxBatch = 50;
    private static readonly TimeSpan TimeoutPorItem = TimeSpan.FromSeconds(5);

    private readonly Random _jitterRng = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "ProcessarWebhookMpPendente iniciado intervaloBase={IntervaloS}s batchMax={Batch}",
            IntervaloBase.TotalSeconds, MaxBatch);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Jitter ±2 s
            var jitterMs = _jitterRng.Next(-2000, 2001);
            var atraso = IntervaloBase + TimeSpan.FromMilliseconds(jitterMs);

            try
            {
                await Task.Delay(atraso, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await ProcessarBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ProcessarWebhookMpPendente erro inesperado no batch.");
            }
        }

        logger.LogInformation("ProcessarWebhookMpPendente encerrado.");
    }

    private async Task ProcessarBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var useCase = scope.ServiceProvider.GetRequiredService<ProcessarWebhookMpUseCase>();

        // Lookup: índice filtrado por status=0 (Received) + ordem por chegada.
        var ids = await db.WebhooksProcessados
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => w.Provider == "mercadopago" && w.Status == WebhookProcessadoStatus.Received)
            .OrderBy(w => w.RecebidoEm)
            .Take(MaxBatch)
            .Select(w => w.Id)
            .ToListAsync(ct);

        if (ids.Count == 0) return;

        logger.LogInformation(
            "ProcessarWebhookMpPendente batch={Count} ids", ids.Count);

        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) break;

            // Timeout agressivo por item — evita 1 webhook lento bloquear o batch inteiro.
            using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            itemCts.CancelAfter(TimeoutPorItem);

            try
            {
                var resultado = await useCase.ExecuteAsync(id, itemCts.Token).ConfigureAwait(false);

                // Persiste UpdateAsync foi tracked pelo use case; força SaveChanges deste scope.
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                logger.LogDebug(
                    "ProcessarWebhookMpPendente webhookId={Id} resultado={Resultado}",
                    id, resultado);
            }
            catch (OperationCanceledException) when (itemCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                logger.LogWarning(
                    "ProcessarWebhookMpPendente timeout webhookId={Id} timeoutS={TimeoutS}",
                    id, TimeoutPorItem.TotalSeconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "ProcessarWebhookMpPendente erro webhookId={Id}", id);
            }
        }
    }
}
