using System.Diagnostics;
using EasyStock.Api.Authorization;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.Services.Notifications.Orchestrators;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

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
    /// Caso contrário, processa todos os shards configurados sequencialmente.
    /// <para>
    /// <b>Recomendação para cron externo:</b> usar <c>?shard=N</c> e disparar 4 chamadas
    /// paralelas (uma por shard 0..3). A versão sem shard processa todos os 4 shards
    /// sequencialmente — sob carga (50 mensagens × 4 shards × ~200ms SMTP médio) pode
    /// levar ~40s, próximo de timeouts típicos de gateway/scheduler externo.
    /// A versão sem shard é destinada a Worker/API in-process onde latência total não
    /// é restrição (loops contínuos).
    /// </para>
    /// </summary>
    /// <param name="shard">Shard alvo [0..ShardCount-1]. Se omitido, processa todos.</param>
    /// <param name="ct">Cancellation token da request.</param>
    [HttpPost("dispatcher/run")]
    [ProducesResponseType(typeof(DispatcherRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ExecutarDispatcher(
        [FromQuery] int? shard,
        CancellationToken ct)
    {
        var opts = options.Value;
        var sw = Stopwatch.StartNew();
        int processadas;

        if (shard.HasValue)
        {
            if (shard.Value < 0 || shard.Value >= opts.ShardCount)
                return BadRequest(new { error = $"shard fora de range [0..{opts.ShardCount - 1}]" });
            processadas = await dispatcherSingleShard.ProcessarBatchAsync(shard.Value, opts.DispatcherBatchSize, ct);
        }
        else
        {
            processadas = await dispatcher.ExecutarRodadaAsync(opts.ShardCount, opts.DispatcherBatchSize, ct);
        }

        sw.Stop();
        logger.LogInformation(
            "[CronJob] dispatcher shard={Shard} processadas={Count} duracaoMs={DuracaoMs}",
            shard?.ToString() ?? "all", processadas, sw.ElapsedMilliseconds);

        return Ok(new DispatcherRunResponse(processadas, shard, sw.ElapsedMilliseconds));
    }

    /// <summary>
    /// Dispara 1 rodada do avaliador (eventos pendentes + cron rotinas).
    /// </summary>
    [HttpPost("avaliador/run")]
    [ProducesResponseType(typeof(JobRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ExecutarAvaliador(CancellationToken ct)
    {
        var opts = options.Value;
        var janela = TimeSpan.FromSeconds(opts.AvaliadorIntervalSeconds * 2);
        var sw = Stopwatch.StartNew();
        await avaliador.ExecutarRodadaAsync(janela, ct);
        sw.Stop();
        logger.LogInformation("[CronJob] avaliador executado duracaoMs={DuracaoMs}", sw.ElapsedMilliseconds);
        return Ok(new JobRunResponse(true, sw.ElapsedMilliseconds));
    }

    /// <summary>
    /// Dispara 1 rodada de coletores de eventos de estado (produtos vencendo, assinaturas, etc.).
    /// </summary>
    [HttpPost("coletor/run")]
    [ProducesResponseType(typeof(JobRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ExecutarColetor(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await coletor.ExecutarRodadaAsync(ct);
        sw.Stop();
        logger.LogInformation("[CronJob] coletor executado duracaoMs={DuracaoMs}", sw.ElapsedMilliseconds);
        return Ok(new JobRunResponse(true, sw.ElapsedMilliseconds));
    }
}

/// <summary>Resposta dos endpoints de gatilho de cron.</summary>
public sealed record JobRunResponse(bool Ok, long DuracaoMs);

/// <summary>Resposta do endpoint dispatcher/run com contagem de mensagens processadas.</summary>
public sealed record DispatcherRunResponse(int Processadas, int? Shard, long DuracaoMs);
