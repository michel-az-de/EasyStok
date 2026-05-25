using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Async.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Diagnostics;
using System.Text.Json;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Worker principal do motor de relatórios.
/// Faz polling de runs pendentes, adquire advisory lock por run (ADR-R02),
/// executa o pipeline stream→export→upload e comita o resultado.
/// Fairness por tenant via round-robin (ADR-R04); lease + heartbeat (ADR-R03).
/// </summary>
public sealed class ReportRunnerBackgroundService(
    IServiceProvider serviceProvider,
    ReportingMetricsService metrics,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    ILogger<ReportRunnerBackgroundService> logger)
    : BackgroundService
{
    // ── Configurações (appsettings.json: "Reporting:Runner") ─────────────────

    private static readonly TimeSpan DefaultPollingInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultHeartbeatPeriod = TimeSpan.FromSeconds(30);
    private const int DefaultBatchSize = 20;
    private const int DefaultMaxConcurrentInstances = 20;

    // Controla paralelismo máximo desta instância.
    private SemaphoreSlim? _instanceSemaphore;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration.GetValue("Reporting:Runner:Enabled", defaultValue: true);
        if (!enabled)
        {
            logger.LogInformation("ReportRunnerBackgroundService desabilitado via Reporting:Runner:Enabled=false.");
            return;
        }

        var pollingInterval = TimeSpan.FromSeconds(configuration.GetValue("Reporting:Runner:PollingIntervalSeconds", 5));
        var batchSize = configuration.GetValue("Reporting:Runner:BatchSize", DefaultBatchSize);
        var maxConcurrent = configuration.GetValue("Reporting:Runner:MaxConcurrentRunsPerInstance", DefaultMaxConcurrentInstances);
        var leaseDuration = TimeSpan.FromMinutes(configuration.GetValue("Reporting:Runner:LeaseDurationMinutes", 5.0));

        _instanceSemaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);

        logger.LogInformation(
            "ReportRunnerBackgroundService iniciado — polling={Interval}s batch={Batch} maxConcurrent={Max}.",
            pollingInterval.TotalSeconds, batchSize, maxConcurrent);

        // Aguarda o app subir antes do primeiro ciclo.
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int dispatched = await DispatchBatchAsync(batchSize, leaseDuration, stoppingToken);

                // Se batch cheio, provavelmente há mais — pola imediatamente.
                if (dispatched >= batchSize) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReportRunnerBackgroundService: erro no ciclo de dispatch.");
            }

            try { await Task.Delay(pollingInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        // Aguarda tasks em voo terminarem.
        for (int i = 0; i < maxConcurrent; i++)
            await _instanceSemaphore.WaitAsync(CancellationToken.None);

        logger.LogInformation("ReportRunnerBackgroundService finalizado.");
    }

    // ── Batch dispatch ────────────────────────────────────────────────────────

    private async Task<int> DispatchBatchAsync(
        int batchSize, TimeSpan leaseDuration, CancellationToken ct)
    {
        IReadOnlyList<Guid> candidates;

        // Pick: consulta runs elegíveis sem lock.
        using (var pickScope = serviceProvider.CreateScope())
        {
            var repo = pickScope.ServiceProvider.GetRequiredService<IReportRunRepository>();
            candidates = await repo.PickNextBatchAsync(batchSize, ct);
        }

        if (candidates.Count == 0) return 0;

        int started = 0;
        foreach (var runId in candidates)
        {
            if (ct.IsCancellationRequested) break;

            // Tenta adquirir semáforo de instância (backpressure local).
            if (!await _instanceSemaphore!.WaitAsync(TimeSpan.Zero, ct))
            {
                logger.LogDebug("ReportRunner: limite máximo de concorrência da instância atingido; run {RunId} aguarda.", runId);
                break;
            }

            // Fire-and-forget com Task.Run para não bloquear o loop.
            _ = Task.Run(async () =>
            {
                try { await TryStartAndExecuteAsync(runId, leaseDuration, ct); }
                finally { _instanceSemaphore.Release(); }
            }, ct);

            started++;
        }

        return started;
    }

    // ── Tentativa de start + execução de uma run ──────────────────────────────

    private async Task TryStartAndExecuteAsync(Guid runId, TimeSpan leaseDuration, CancellationToken ct)
    {
        ReportRun? run;

        // Transação de start: advisory lock + TryStart + commit.
        using (var startScope = serviceProvider.CreateScope())
        {
            var db = startScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var repo = startScope.ServiceProvider.GetRequiredService<IReportRunRepository>();

            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            bool locked = await repo.TryAcquireLockAsync(runId, ct);
            if (!locked)
            {
                logger.LogDebug("ReportRunner: advisory lock falhou para run {RunId} — outra instância pegou.", runId);
                await tx.RollbackAsync(ct);
                return;
            }

            run = await repo.GetByIdAsync(runId, ct);
            if (run is null || !run.TryStart(leaseDuration))
            {
                logger.LogDebug("ReportRunner: run {RunId} já foi processada ou está em estado inválido.", runId);
                await tx.RollbackAsync(ct);
                return;
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Métricas de início.
        var contextoTag = run.Contexto == ReportContexto.AdminSaaS ? "admin" : "tenant";
        metrics.RecordStarted(contextoTag, run.ReportKey);

        // Execução fora da transação de start.
        await ExecuteRunAsync(run, leaseDuration, ct);
    }

    // ── Pipeline de execução (§7 do plano) ───────────────────────────────────

    private async Task ExecuteRunAsync(ReportRun run, TimeSpan leaseDuration, CancellationToken stoppingToken)
    {
        using var execScope = serviceProvider.CreateScope();

        var scope = execScope.ServiceProvider.GetRequiredService<IReportExecutionScope>();
        var registry = execScope.ServiceProvider.GetRequiredService<ReportRegistry>();
        var exporterFactory = execScope.ServiceProvider.GetRequiredService<ReportExporterResolver>();
        var storage = execScope.ServiceProvider.GetRequiredService<IFileStorage>();
        var repo = execScope.ServiceProvider.GetRequiredService<IReportRunRepository>();
        var db = execScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var definition = registry.Get(run.ReportKey);

        // Cancellation combinada: stoppingToken do host + token de Canceling do run.
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var runCt = runCts.Token;

        // Contexto do tenant (ADR-R06).
        using var execContext = scope.Begin(run.EmpresaId, run.UsuarioSolicitanteId, run.Contexto);

        // Storage key conforme convenção B-15.
        var ext = exporterFactory.Resolve(run.Format).FileExtension;
        var storageKey = run.Contexto == ReportContexto.AdminSaaS
            ? $"admin/reports/{run.EnqueuedAt:yyyy}/{run.EnqueuedAt:MM}/{run.Id}{ext}"
            : $"tenants/{run.EmpresaId}/reports/{run.EnqueuedAt:yyyy}/{run.EnqueuedAt:MM}/{run.Id}{ext}";

        var contentType = exporterFactory.Resolve(run.Format).ContentType;

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ReportRunId"] = run.Id,
            ["ReportKey"] = run.ReportKey,
            ["TenantId"] = run.EmpresaId?.ToString() ?? "admin",
        });

        logger.LogInformation("ReportRunner: iniciando execução de run {RunId} (key={Key}).", run.Id, run.ReportKey);

        var execSw = Stopwatch.StartNew();
        var waitMs = run.StartedAt.HasValue
            ? (long)(run.StartedAt.Value - run.EnqueuedAt).TotalMilliseconds
            : 0L;
        var contextoTag = run.Contexto == ReportContexto.AdminSaaS ? "admin" : "tenant";

        // Heartbeat timer — estende lease a cada 30s.
        using var heartbeatTimer = new PeriodicTimer(DefaultHeartbeatPeriod);
        var heartbeatTask = Task.Run(async () =>
        {
            try
            {
                while (await heartbeatTimer.WaitForNextTickAsync(runCt))
                {
                    // Verifica se run ainda quer ser cancelada (status Canceling).
                    using var hbScope = serviceProvider.CreateScope();
                    var hbRepo = hbScope.ServiceProvider.GetRequiredService<IReportRunRepository>();
                    var current = await hbRepo.GetByIdAsync(run.Id, CancellationToken.None);
                    if (current?.Status == ReportStatus.Canceling)
                    {
                        logger.LogInformation("ReportRunner: run {RunId} entrou em Canceling — cancelando via token.", run.Id);
                        runCts.Cancel();
                        return;
                    }
                    await hbRepo.HeartbeatAsync(run.Id, leaseDuration, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) { /* esperado */ }
        }, CancellationToken.None);

        long rowCount = 0;
        try
        {
            // Resolve handler de forma não-tipada via reflection (handler armazenado por tipo dinâmico).
            var handlerType = typeof(IReportHandler<,>).MakeGenericType(definition.ParamsType, definition.RowType);
            dynamic handler = execScope.ServiceProvider.GetRequiredService(handlerType);
            dynamic paramsObj = handler.DeserializeParams(run.ParamsJson);

            await handler.ValidateAsync(paramsObj, runCt);

            var exporter = exporterFactory.Resolve(run.Format);
            var schema = (ReportSchema)handler.GetSchema(paramsObj);
            var options = new ReportExportOptions();

            await using var uploadStream = await storage.OpenUploadStreamAsync(storageKey, contentType, runCt);
            await using var hashStream = new Infra.Async.Reporting.HashingCountingStream(uploadStream);

            // rowsEnumerable é IAsyncEnumerable<TRow> — o exporter o consome em streaming.
            var rowsEnumerable = (IAsyncEnumerable<object>)ConvertToObjectEnumerable(
                handler.StreamAsync(paramsObj, runCt), definition.RowType);

            await exporter.WriteAsync(
                rowsEnumerable,
                schema,
                hashStream,
                options,
                runCt,
                onRowFlushed: () => Interlocked.Increment(ref rowCount));

            await hashStream.FlushAsync(runCt);

            var hexHash = hashStream.GetHexHash();
            var sizeBytes = hashStream.BytesWritten;

            // Commit: marca run como Succeeded em transação.
            await using var tx = await db.Database.BeginTransactionAsync(CancellationToken.None);
            run.MarkSucceeded(storageKey, sizeBytes, hexHash, rowCount);
            db.Entry(run).State = EntityState.Modified;
            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);

            logger.LogInformation(
                "ReportRunner: run {RunId} concluída — rows={Rows} bytes={Bytes}.",
                run.Id, rowCount, sizeBytes);

            metrics.RecordCompleted(
                contextoTag, run.ReportKey, "Succeeded",
                waitMs, execSw.ElapsedMilliseconds,
                rowCount, sizeBytes, run.Format.ToString());

            // Notificação RelatorioPronto (somente Tenant — AdminSaaS tem EmpresaId nulo).
            if (run.EmpresaId.HasValue)
            {
                try
                {
                    var notif = execScope.ServiceProvider.GetRequiredService<INotificadorService>();
                    var payload = JsonSerializer.Serialize(new
                    {
                        reportKey = run.ReportKey,
                        reportLabel = definition.Label,
                        runId = run.Id,
                        format = run.Format.ToString(),
                        rowCount,
                    });
                    await notif.PublicarEventoAsync(
                        TipoEventoNotificacao.RelatorioPronto,
                        run.EmpresaId.Value,
                        run.UsuarioSolicitanteId,
                        payload,
                        ct: CancellationToken.None);
                }
                catch (Exception notifEx)
                {
                    logger.LogWarning(notifEx,
                        "ReportRunner: falha ao publicar notificação RelatorioPronto para run {RunId}.", run.Id);
                }
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested || runCts.IsCancellationRequested)
        {
            // Cancelamento solicitado pelo usuário (status Canceling) ou host shutdown.
            logger.LogInformation("ReportRunner: run {RunId} cancelada.", run.Id);
            run.MarkCanceled();
            await SafeSaveAndCleanupAsync(db, run, storage, storageKey);
            metrics.RecordCompleted(
                contextoTag, run.ReportKey, "Canceled",
                waitMs, execSw.ElapsedMilliseconds, rowCount, 0L, run.Format.ToString());
        }
        catch (Exception ex)
        {
            var errorClass = ex.GetType().FullName ?? ex.GetType().Name;
            var friendlyMsg = MapErrorToFriendlyMessage(ex);
            var isTerminal = IsTerminalFailure(ex);

            logger.LogError(ex,
                "ReportRunner: run {RunId} falhou (terminal={Terminal}, class={Class}).",
                run.Id, isTerminal, errorClass);

            run.MarkFailed(errorClass, friendlyMsg, isTerminal);
            await SafeSaveAndCleanupAsync(db, run, storage, storageKey);
            metrics.RecordCompleted(
                contextoTag, run.ReportKey, isTerminal ? "Failed" : "FailedTransient",
                waitMs, execSw.ElapsedMilliseconds, rowCount, 0L, run.Format.ToString());

            // Notificação RelatorioFalhou somente para falhas terminais (evita spam em retries).
            if (isTerminal && run.EmpresaId.HasValue)
            {
                try
                {
                    var notif = execScope.ServiceProvider.GetRequiredService<INotificadorService>();
                    var payload = JsonSerializer.Serialize(new
                    {
                        reportKey = run.ReportKey,
                        reportLabel = definition.Label,
                        runId = run.Id,
                        errorMensagem = friendlyMsg,
                    });
                    await notif.PublicarEventoAsync(
                        TipoEventoNotificacao.RelatorioFalhou,
                        run.EmpresaId.Value,
                        run.UsuarioSolicitanteId,
                        payload,
                        ct: CancellationToken.None);
                }
                catch (Exception notifEx)
                {
                    logger.LogWarning(notifEx,
                        "ReportRunner: falha ao publicar notificação RelatorioFalhou para run {RunId}.", run.Id);
                }
            }
        }
        finally
        {
            runCts.Cancel();          // Para o heartbeat timer.
            heartbeatTimer.Dispose();
            await heartbeatTask;      // Aguarda encerramento limpo.
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converte IAsyncEnumerable&lt;TRow&gt; em IAsyncEnumerable&lt;object&gt;
    /// para que os exporters possam operar de forma não-tipada quando necessário.
    /// Na prática os exporters são genéricos e TRow correto é passado via WriteAsync&lt;TRow&gt;.
    /// </summary>
    private static async IAsyncEnumerable<object> ConvertToObjectEnumerable<T>(
        IAsyncEnumerable<T> source,
        Type _rowType)
    {
        await foreach (var item in source)
            yield return item!;
    }

    private static async Task SafeSaveAndCleanupAsync(
        EasyStockDbContext db, ReportRun run, IFileStorage storage, string storageKey)
    {
        try
        {
            db.Entry(run).State = EntityState.Modified;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception saveEx)
        {
            Log.Error(saveEx, "ReportRunner: falha ao salvar estado final da run {RunId}.", run.Id);
        }

        // Rollback compensatório: remove artefato parcialmente gravado.
        try { await storage.DeleteAsync(storageKey, CancellationToken.None); }
        catch { /* melhor esforço */ }
    }

    private static bool IsTerminalFailure(Exception ex) =>
        ex is ArgumentException or NotSupportedException
        || (ex is InvalidOperationException && (ex.Message.Contains("permissão") || ex.Message.Contains("acesso")));

    private static string MapErrorToFriendlyMessage(Exception ex) => ex switch
    {
        OperationCanceledException => "O relatório demorou mais que o esperado.",
        InvalidOperationException { Message: var m } when m.Contains("timeout")
            || m.Contains("Timeout") => "O relatório demorou mais que o esperado. Tente um período menor.",
        OutOfMemoryException => "O volume de dados é grande demais para um único arquivo. Divida em períodos menores.",
        ArgumentException { Message: var m } => $"Parâmetros inválidos: {m}",
        _ => "Algo deu errado na geração. Tente de novo.",
    };

    // Referência estática ao Serilog sem injetar ILogger (usado no catch de cleanup).
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ReportRunnerBackgroundService>();
}

/// <summary>
/// Factory que resolve o <see cref="IReportExporter"/> correto para cada <see cref="ReportFormat"/>.
/// </summary>
public sealed class ReportExporterResolver(IEnumerable<IReportExporter> exporters)
{
    private readonly Dictionary<ReportFormat, IReportExporter> _map =
        exporters.ToDictionary(e => e.Format);

    public IReportExporter Resolve(ReportFormat format) =>
        _map.TryGetValue(format, out var e)
            ? e
            : throw new NotSupportedException($"Formato de exportação '{format}' não registrado.");
}
