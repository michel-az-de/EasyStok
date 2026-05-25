using EasyStock.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EasyStock.Infra.Postgre.Data.Configurations.Reporting;

public class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> b)
    {
        b.ToTable("report_runs");
        b.HasKey(r => r.Id);

        // ── Identidade ──────────────────────────────────────────────────────
        b.Property(r => r.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        b.Property(r => r.EmpresaId)
            .HasColumnName("empresa_id")
            .HasColumnType("uuid");
        // nullable — null quando contexto = AdminSaaS

        b.Property(r => r.UsuarioSolicitanteId)
            .HasColumnName("usuario_solicitante_id")
            .HasColumnType("uuid")
            .IsRequired();

        b.Property(r => r.ReportKey)
            .HasColumnName("report_key")
            .HasMaxLength(120)
            .IsRequired();

        b.Property(r => r.MotivoExecucao)
            .HasColumnName("motivo_execucao")
            .HasMaxLength(500);

        // ── Enums (smallint) ────────────────────────────────────────────────
        b.Property(r => r.Categoria)
            .HasColumnName("categoria")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .IsRequired();

        b.Property(r => r.Contexto)
            .HasColumnName("contexto")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .IsRequired();

        b.Property(r => r.Format)
            .HasColumnName("format")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .IsRequired();

        b.Property(r => r.Status)
            .HasColumnName("status")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .IsRequired();

        // ── Parâmetros ──────────────────────────────────────────────────────
        b.Property(r => r.ParamsJson)
            .HasColumnName("params_json")
            .HasColumnType("jsonb")
            .IsRequired();

        b.Property(r => r.ParamsHash)
            .HasColumnName("params_hash")
            .HasMaxLength(64)
            .IsRequired();

        b.Property(r => r.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(80);

        b.Property(r => r.SemanticVersion)
            .HasColumnName("semantic_version")
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue("1.0");

        // ── Artefato ────────────────────────────────────────────────────────
        b.Property(r => r.ArtifactStorageKey)
            .HasColumnName("artifact_storage_key")
            .HasMaxLength(512);

        b.Property(r => r.ArtifactSizeBytes)
            .HasColumnName("artifact_size_bytes");

        b.Property(r => r.ArtifactSha256)
            .HasColumnName("artifact_sha256")
            .HasMaxLength(64);

        b.Property(r => r.RowCount)
            .HasColumnName("row_count");

        b.Property(r => r.WarningsJson)
            .HasColumnName("warnings_json")
            .HasColumnType("jsonb");

        // ── Erro ────────────────────────────────────────────────────────────
        b.Property(r => r.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        b.Property(r => r.ErrorClass)
            .HasColumnName("error_class")
            .HasMaxLength(255);

        // ── Tentativas ──────────────────────────────────────────────────────
        b.Property(r => r.Tentativas)
            .HasColumnName("tentativas")
            .IsRequired()
            .HasDefaultValue(0);

        b.Property(r => r.MaxTentativas)
            .HasColumnName("max_tentativas")
            .IsRequired()
            .HasDefaultValue(3);

        // ── Timestamps ──────────────────────────────────────────────────────
        b.Property(r => r.EnqueuedAt)
            .HasColumnName("enqueued_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        b.Property(r => r.StartedAt)
            .HasColumnName("started_at")
            .HasColumnType("timestamp with time zone");

        b.Property(r => r.FinishedAt)
            .HasColumnName("finished_at")
            .HasColumnType("timestamp with time zone");

        b.Property(r => r.LeaseExpiresAt)
            .HasColumnName("lease_expires_at")
            .HasColumnType("timestamp with time zone");

        b.Property(r => r.NextAttemptAt)
            .HasColumnName("next_attempt_at")
            .HasColumnType("timestamp with time zone");

        b.Property(r => r.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");

        // Colunas de auditoria padrão (AuditTimestampsInterceptor as preenche)
        b.Property<DateTimeOffset>("CriadoEm")
            .HasColumnName("criado_em")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        b.Property<DateTimeOffset>("AlteradoEm")
            .HasColumnName("alterado_em")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // ── xmin: concurrency token (Npgsql system column) ─────────────────
        // B-05: usar IsConcurrencyToken() com ValueGeneratedOnAddOrUpdate
        // (não .IsRowVersion(), que é SQL Server specific)
        b.Property(r => r.Xmin)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // ── Índices (não-CONCURRENTLY — CONCURRENTLY vai na migration) ──────
        // ix_report_runs_user_listing — listagem "minhas execuções"
        b.HasIndex(r => new { r.EmpresaId, r.UsuarioSolicitanteId, r.EnqueuedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_report_runs_user_listing");

        // ix_report_runs_idem — idempotência
        // Partial index (onde idempotency_key IS NOT NULL): não suportado
        // diretamente por HasIndex + EF Core para CONCURRENTLY, vai na migration.

        // Sem QueryFilter: ReportRun tem EmpresaId nullable (contexto Admin).
        // Filtro manual em WorkerCurrentUserAccessor + ITenantScopedQueryBuilder.
    }
}
