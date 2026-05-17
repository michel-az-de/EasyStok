using EasyStock.Domain.Reporting.Exceptions;

namespace EasyStock.Domain.Reporting;

/// <summary>
/// Agregado que representa uma execução assíncrona de relatório.
/// </summary>
/// <remarks>
/// Invariantes:
/// 1. <see cref="Status"/> segue a máquina: Pending → Running → (Succeeded | Failed | Canceled);
///    Pending → Canceling não aplicável (só Running → Canceling); Pending → Canceled direto.
/// 2. <see cref="Succeeded"/> exige ArtifactStorageKey != null, ArtifactSizeBytes &gt; 0, ArtifactSha256 != null.
/// 3. <see cref="Tentativas"/> &lt;= MaxTentativas + 1.
/// 4. <see cref="EmpresaId"/> é null apenas quando <see cref="Contexto"/> == AdminSaaS.
/// </remarks>
public sealed class ReportRun
{
    private ReportRun() { } // EF Core

    public Guid Id { get; private set; }

    /// <summary>Tenant dono da execução. Null em contexto AdminSaaS.</summary>
    public Guid? EmpresaId { get; private set; }

    /// <summary>Usuário que solicitou (ou ID do admin SaaS).</summary>
    public Guid UsuarioSolicitanteId { get; private set; }

    /// <summary>Chave técnica do relatório (ex: "vendas.por-periodo").</summary>
    public string ReportKey { get; private set; } = string.Empty;

    public ReportCategoria Categoria { get; private set; }
    public ReportContexto Contexto { get; private set; }

    /// <summary>Parâmetros serializados em JSON.</summary>
    public string ParamsJson { get; private set; } = string.Empty;

    /// <summary>Hash SHA-256 hex de (ReportKey + ParamsJson + Format) — para idempotência fraca.</summary>
    public string ParamsHash { get; private set; } = string.Empty;

    /// <summary>Chave de idempotência forte fornecida pelo cliente (opcional).</summary>
    public string? IdempotencyKey { get; private set; }

    public ReportFormat Format { get; private set; }

    /// <summary>Versão semântica do handler (MAJOR.MINOR). Snapshottado no enqueue.</summary>
    public string SemanticVersion { get; private set; } = "1.0";

    public ReportStatus Status { get; private set; }

    public string? ArtifactStorageKey { get; private set; }
    public long? ArtifactSizeBytes { get; private set; }
    public string? ArtifactSha256 { get; private set; }
    public long? RowCount { get; private set; }

    /// <summary>Mensagem amigável mapeada pelo Worker (não expõe stack trace).</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>FQN da exceção original (para métricas/telemetria).</summary>
    public string? ErrorClass { get; private set; }

    /// <summary>JSON com avisos gerados durante a execução (por ex. dados legados).</summary>
    public string? WarningsJson { get; private set; }

    public int Tentativas { get; private set; }
    public int MaxTentativas { get; private set; }

    /// <summary>Motivo da execução — obrigatório em contexto AdminSaaS para auditoria LGPD.</summary>
    public string? MotivoExecucao { get; private set; }

    public DateTimeOffset EnqueuedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Quando o artefato expira (retenção de 30 dias).</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Concurrency token (xmin do Postgres).</summary>
    public uint Xmin { get; private set; }

    // ── Factory ──────────────────────────────────────────────────────────────

    public static ReportRun Enqueue(
        Guid empresaId,
        Guid usuarioId,
        string reportKey,
        ReportCategoria categoria,
        ReportContexto contexto,
        string paramsJson,
        string paramsHash,
        ReportFormat format,
        string semanticVersion,
        TimeSpan retention,
        int maxTentativas = 3,
        string? idempotencyKey = null,
        string? motivoExecucao = null)
    {
        if (reportKey.Length > 120)
            throw new ArgumentException("ReportKey excede 120 caracteres.", nameof(reportKey));
        if (paramsJson.Length > 65_536)
            throw new ArgumentException("ParamsJson excede 64 KB.", nameof(paramsJson));
        if (contexto == ReportContexto.AdminSaaS && string.IsNullOrWhiteSpace(motivoExecucao))
            throw new ArgumentException("MotivoExecucao é obrigatório em contexto AdminSaaS.", nameof(motivoExecucao));
        if (contexto == ReportContexto.AdminSaaS && empresaId != Guid.Empty)
            throw new ArgumentException("EmpresaId deve ser Empty em contexto AdminSaaS.", nameof(empresaId));

        var now = DateTimeOffset.UtcNow;
        return new ReportRun
        {
            Id = Guid.NewGuid(),
            EmpresaId = contexto == ReportContexto.AdminSaaS ? null : empresaId,
            UsuarioSolicitanteId = usuarioId,
            ReportKey = reportKey,
            Categoria = categoria,
            Contexto = contexto,
            ParamsJson = paramsJson,
            ParamsHash = paramsHash,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            Format = format,
            SemanticVersion = semanticVersion,
            Status = ReportStatus.Pending,
            Tentativas = 0,
            MaxTentativas = maxTentativas,
            MotivoExecucao = motivoExecucao?.Trim(),
            EnqueuedAt = now,
            ExpiresAt = now + retention,
        };
    }

    // ── State transitions ─────────────────────────────────────────────────────

    /// <summary>
    /// Tenta iniciar a execução. Retorna false se o status não for Pending
    /// (outra instância pode ter pegado antes).
    /// </summary>
    public bool TryStart(TimeSpan leaseDuration)
    {
        if (Status != ReportStatus.Pending) return false;
        Status = ReportStatus.Running;
        StartedAt = DateTimeOffset.UtcNow;
        LeaseExpiresAt = StartedAt.Value + leaseDuration;
        Tentativas++;
        return true;
    }

    /// <summary>Estende o lease. Lança se status não for Running.</summary>
    public void Heartbeat(TimeSpan leaseDuration)
    {
        if (Status != ReportStatus.Running)
            throw new InvalidReportStateException(Status, "Heartbeat");
        LeaseExpiresAt = DateTimeOffset.UtcNow + leaseDuration;
    }

    public void MarkSucceeded(string storageKey, long sizeBytes, string sha256, long rowCount, string? warningsJson = null)
    {
        if (Status != ReportStatus.Running && Status != ReportStatus.Canceling)
            throw new InvalidReportStateException(Status, "MarkSucceeded");
        Status = ReportStatus.Succeeded;
        ArtifactStorageKey = storageKey;
        ArtifactSizeBytes = sizeBytes;
        ArtifactSha256 = sha256;
        RowCount = rowCount;
        WarningsJson = warningsJson;
        FinishedAt = DateTimeOffset.UtcNow;
        LeaseExpiresAt = null;
    }

    public void MarkFailed(string errorClass, string friendlyMessage, bool terminal)
    {
        if (Status != ReportStatus.Running && Status != ReportStatus.Canceling)
            throw new InvalidReportStateException(Status, "MarkFailed");
        ErrorClass = errorClass;
        ErrorMessage = friendlyMessage;
        FinishedAt = DateTimeOffset.UtcNow;
        LeaseExpiresAt = null;
        if (terminal || Tentativas >= MaxTentativas)
        {
            Status = ReportStatus.Failed;
        }
        else
        {
            // Volta para Pending com backoff exponencial ± 20% jitter.
            Status = ReportStatus.Pending;
            var baseDelay = TimeSpan.FromSeconds(30 * Math.Pow(2, Tentativas - 1));
            var jitter = TimeSpan.FromSeconds((new Random().NextDouble() * 12) - 6);
            NextAttemptAt = DateTimeOffset.UtcNow + baseDelay + jitter;
        }
    }

    public void MarkCanceled()
    {
        if (Status != ReportStatus.Pending && Status != ReportStatus.Running && Status != ReportStatus.Canceling)
            throw new InvalidReportStateException(Status, "MarkCanceled");
        Status = ReportStatus.Canceled;
        FinishedAt = DateTimeOffset.UtcNow;
        LeaseExpiresAt = null;
    }

    public void RequestCancellation()
    {
        if (Status == ReportStatus.Pending)
        {
            MarkCanceled();
            return;
        }
        if (Status != ReportStatus.Running)
            throw new InvalidReportStateException(Status, "RequestCancellation");
        Status = ReportStatus.Canceling;
    }

    /// <summary>Zera estado de erro e re-enfileira (uso admin).</summary>
    public void ResetForRetry()
    {
        if (Status != ReportStatus.Failed && Status != ReportStatus.Canceled)
            throw new InvalidReportStateException(Status, "ResetForRetry");
        Status = ReportStatus.Pending;
        ErrorClass = null;
        ErrorMessage = null;
        Tentativas = 0;
        NextAttemptAt = null;
        FinishedAt = null;
        StartedAt = null;
        LeaseExpiresAt = null;
    }

    /// <summary>
    /// Chamado pelo watchdog quando o lease expirou. Volta para Pending com backoff.
    /// </summary>
    public void ReclaimLease()
    {
        if (Status != ReportStatus.Running && Status != ReportStatus.Canceling)
            throw new InvalidReportStateException(Status, "ReclaimLease");

        if (Tentativas >= MaxTentativas)
        {
            Status = ReportStatus.Failed;
            ErrorClass = "LeaseLossTerminal";
            ErrorMessage = "Excedeu o número máximo de tentativas após o Worker não confirmar a execução.";
            FinishedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            Status = ReportStatus.Pending;
            var baseDelay = TimeSpan.FromSeconds(30 * Math.Pow(2, Tentativas - 1));
            var jitter = TimeSpan.FromSeconds((new Random().NextDouble() * 12) - 6);
            NextAttemptAt = DateTimeOffset.UtcNow + baseDelay + jitter;
        }
        LeaseExpiresAt = null;
    }

    /// <summary>
    /// Marca o artefato como purgado após expiração (GC de storage).
    /// O status permanece Succeeded; ArtifactStorageKey = null indica "expirado".
    /// </summary>
    public void MarkArtifactPurged()
    {
        if (Status != ReportStatus.Succeeded)
            throw new InvalidReportStateException(Status, "MarkArtifactPurged");
        ArtifactStorageKey = null;
        ArtifactSizeBytes = null;
        ArtifactSha256 = null;
    }
}
