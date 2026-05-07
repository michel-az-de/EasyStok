using EasyStock.Api.Authorization;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using EasyStock.Infra.Notifications.Hosting;

namespace EasyStock.Api.Controllers.Internal;

/// <summary>
/// Endpoints internos de gatilho HTTP do pipeline de notificações.
/// Acionados por scheduler externo (K8s CronJob, GitHub Actions schedule, Azure Scheduler).
/// Idempotentes — advisory lock previne dupla entrega entre instâncias/modos.
/// Autenticação via header <c>X-Internal-Cron-Token</c> (policy <c>InternalCronJob</c>).
/// Rate limit "geral" (200 req/min) — escolhido por simplicidade; cron externo dispara
/// poucas vezes por hora.
/// </summary>
[ApiController]
[Route("api/internal/notif-jobs")]
[Authorize(Policy = InternalCronJobAuthHandler.PolicyName)]
[EnableRateLimiting("geral")]
public sealed class NotificacoesJobsController(
    INotificacoesDispatcherOrchestrator dispatcher,
    INotificationDispatcher dispatcherSingleShard,
    INotificacoesAvaliadorOrchestrator avaliador,
    INotificacoesColetorOrchestrator coletor,
    IOptions<NotificationsHostingOptions> options,
    ILogger<NotificacoesJobsController> logger) : ControllerBase
{
    /// <summary>
    /// Dispara 1 rodada do dispatcher. Se <paramref name="shard"/> for informado, processa
    /// apenas aquele shard (granularidade fina para schedulers que querem paralelizar).
    /// Caso contrário, processa todos os shards configurados.
    /// </summary>
    [HttpPost("dispatcher/run")]
    public async Task<IActionResult> ExecutarDispatcher(
        [FromQuery] int? shard,
        CancellationToken ct)
    {
        var opts = options.Value;
        int processadas;
        if (shard.HasValue)
        {
            if (shard.Value < 0 || shard.Value >= opts.ShardCount)
                return BadRequest(new { error = $"shard fora de range [0..{opts.ShardCount - 1}]" });
            processadas = await dispatcherSingleShard.ProcessarBatchAsync(shard.Value, opts.DispatcherBatchSize, ct);
            logger.LogInformation("[CronJob] dispatcher shard={Shard} processadas={Count}", shard.Value, processadas);
        }
        else
        {
            processadas = await dispatcher.ExecutarRodadaAsync(opts.ShardCount, opts.DispatcherBatchSize, ct);
            logger.LogInformation("[CronJob] dispatcher all shards — processadas={Count}", processadas);
        }
        return Ok(new { processadas, shard });
    }

    /// <summary>
    /// Dispara 1 rodada do avaliador (eventos pendentes + cron rotinas).
    /// </summary>
    [HttpPost("avaliador/run")]
    public async Task<IActionResult> ExecutarAvaliador(CancellationToken ct)
    {
        var opts = options.Value;
        var janela = TimeSpan.FromSeconds(opts.AvaliadorIntervalSeconds * 2);
        await avaliador.ExecutarRodadaAsync(janela, ct);
        logger.LogInformation("[CronJob] avaliador executado");
        return Ok(new { ok = true });
    }

    /// <summary>
    /// Dispara 1 rodada de coletores de eventos de estado (produtos vencendo, assinaturas, etc.).
    /// </summary>
    [HttpPost("coletor/run")]
    public async Task<IActionResult> ExecutarColetor(CancellationToken ct)
    {
        await coletor.ExecutarRodadaAsync(ct);
        logger.LogInformation("[CronJob] coletor executado");
        return Ok(new { ok = true });
    }
}
