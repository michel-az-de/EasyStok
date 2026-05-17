using System.Diagnostics.Metrics;

namespace EasyStock.Infra.Async.Reporting;

/// <summary>
/// Métricas do motor de relatórios — prefixo <c>easystock.reporting.*</c>.
/// Segue o padrão de <c>MetricsService</c> (EasyStock.Api) usando IMeterFactory.
/// Registrado como Singleton tanto no Worker quanto na API.
/// </summary>
public sealed class ReportingMetricsService
{
    // Runs
    private readonly Counter<long>   _enqueued;
    private readonly Counter<long>   _started;
    private readonly Counter<long>   _completed;
    private readonly Histogram<long> _waitMs;
    private readonly Histogram<long> _execMs;
    private readonly Histogram<long> _rowsExported;
    private readonly Histogram<long> _artifactBytes;

    // Leases / GC
    private readonly Counter<long>   _leasesReclaimed;
    private readonly Counter<long>   _artifactsPurged;

    // Preview / /data
    private readonly Counter<long>   _previewRequested;
    private readonly Counter<long>   _previewTimedOut;
    private readonly Counter<long>   _dataRequested;

    // Quick Reports (mobile)
    private readonly Counter<long>   _quickRequested;
    private readonly Histogram<long> _quickLatencyMs;

    // Notificações
    private readonly Counter<long>   _notificationsPublished;

    public ReportingMetricsService(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("EasyStock.Reporting");

        _enqueued = meter.CreateCounter<long>(
            "easystock.reporting.runs.enqueued.total",
            description: "Execuções de relatório enfileiradas");

        _started = meter.CreateCounter<long>(
            "easystock.reporting.runs.started.total",
            description: "Execuções de relatório iniciadas pelo Worker");

        _completed = meter.CreateCounter<long>(
            "easystock.reporting.runs.completed.total",
            description: "Execuções concluídas (Succeeded | Failed | Canceled)");

        _waitMs = meter.CreateHistogram<long>(
            "easystock.reporting.runs.wait.ms",
            unit: "ms",
            description: "Tempo de espera na fila (started_at − enqueued_at)");

        _execMs = meter.CreateHistogram<long>(
            "easystock.reporting.runs.exec.ms",
            unit: "ms",
            description: "Tempo de execução do handler (finished_at − started_at)");

        _rowsExported = meter.CreateHistogram<long>(
            "easystock.reporting.runs.rows.exported",
            description: "Número de linhas geradas por execução concluída");

        _artifactBytes = meter.CreateHistogram<long>(
            "easystock.reporting.artifacts.size.bytes",
            unit: "bytes",
            description: "Tamanho do artefato gerado em bytes");

        _leasesReclaimed = meter.CreateCounter<long>(
            "easystock.reporting.leases.reclaimed.total",
            description: "Leases expiradas recuperadas pelo Watchdog");

        _artifactsPurged = meter.CreateCounter<long>(
            "easystock.reporting.artifacts.purged.total",
            description: "Artefatos removidos pelo GC (Expired | OrphanGC | TenantLgpd)");

        _previewRequested = meter.CreateCounter<long>(
            "easystock.reporting.preview.requested.total",
            description: "Pré-visualizações solicitadas (POST /preview)");

        _previewTimedOut = meter.CreateCounter<long>(
            "easystock.reporting.preview.timed_out.total",
            description: "Pré-visualizações canceladas por timeout (> 3 s)");

        _dataRequested = meter.CreateCounter<long>(
            "easystock.reporting.data.requested.total",
            description: "Chamadas ao endpoint síncrono POST /data");

        _quickRequested = meter.CreateCounter<long>(
            "easystock.reporting.quick.requested.total",
            description: "Quick Reports mobile solicitados");

        _quickLatencyMs = meter.CreateHistogram<long>(
            "easystock.reporting.quick.latency.ms",
            unit: "ms",
            description: "Latência dos Quick Reports (p95 alvo: < 800 ms)");

        _notificationsPublished = meter.CreateCounter<long>(
            "easystock.reporting.notifications.published.total",
            description: "Eventos de notificação publicados após conclusão de run");
    }

    // ── Runs ─────────────────────────────────────────────────────────────────

    public void RecordEnqueued(string contexto, string reportKey, string format) =>
        _enqueued.Add(1,
            new KeyValuePair<string, object?>("contexto", contexto),
            new KeyValuePair<string, object?>("report_key", reportKey),
            new KeyValuePair<string, object?>("format", format));

    public void RecordStarted(string contexto, string reportKey) =>
        _started.Add(1,
            new KeyValuePair<string, object?>("contexto", contexto),
            new KeyValuePair<string, object?>("report_key", reportKey));

    public void RecordCompleted(
        string contexto, string reportKey, string status,
        long waitMs, long execMs, long rows, long artifactBytes, string format)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("contexto",    contexto),
            new KeyValuePair<string, object?>("report_key",  reportKey),
            new KeyValuePair<string, object?>("status",      status),
        };

        _completed.Add(1, tags);
        _waitMs.Record(waitMs,
            new KeyValuePair<string, object?>("contexto",   contexto),
            new KeyValuePair<string, object?>("report_key", reportKey));
        _execMs.Record(execMs,
            new KeyValuePair<string, object?>("contexto",   contexto),
            new KeyValuePair<string, object?>("report_key", reportKey));

        if (rows > 0)
            _rowsExported.Record(rows,
                new KeyValuePair<string, object?>("contexto",   contexto),
                new KeyValuePair<string, object?>("report_key", reportKey));

        if (artifactBytes > 0)
            _artifactBytes.Record(artifactBytes,
                new KeyValuePair<string, object?>("contexto",   contexto),
                new KeyValuePair<string, object?>("report_key", reportKey),
                new KeyValuePair<string, object?>("format",     format));
    }

    // ── Lease / GC ───────────────────────────────────────────────────────────

    public void RecordLeasesReclaimed(string contexto, int count) =>
        _leasesReclaimed.Add(count,
            new KeyValuePair<string, object?>("contexto", contexto));

    public void RecordArtifactsPurged(string contexto, string reason, int count) =>
        _artifactsPurged.Add(count,
            new KeyValuePair<string, object?>("contexto", contexto),
            new KeyValuePair<string, object?>("reason",   reason));

    // ── Preview / /data ──────────────────────────────────────────────────────

    public void RecordPreviewRequested(string reportKey) =>
        _previewRequested.Add(1,
            new KeyValuePair<string, object?>("report_key", reportKey));

    public void RecordPreviewTimedOut(string reportKey) =>
        _previewTimedOut.Add(1,
            new KeyValuePair<string, object?>("report_key", reportKey));

    public void RecordDataRequested(string contexto, string reportKey) =>
        _dataRequested.Add(1,
            new KeyValuePair<string, object?>("contexto",   contexto),
            new KeyValuePair<string, object?>("report_key", reportKey));

    // ── Quick Reports ─────────────────────────────────────────────────────────

    public void RecordQuickRequested(string quickKey) =>
        _quickRequested.Add(1,
            new KeyValuePair<string, object?>("quick_key", quickKey));

    public void RecordQuickLatency(string quickKey, long ms) =>
        _quickLatencyMs.Record(ms,
            new KeyValuePair<string, object?>("quick_key", quickKey));

    // ── Notificações ──────────────────────────────────────────────────────────

    public void RecordNotificationPublished(string tipoEvento) =>
        _notificationsPublished.Add(1,
            new KeyValuePair<string, object?>("tipo_evento", tipoEvento));
}
