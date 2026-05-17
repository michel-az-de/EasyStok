using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EasyStock.Infra.Postgre.Migrations
{
    /// <inheritdoc />
    public partial class AddReportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Tabela principal ──────────────────────────────────────────────────────
            // xmin é coluna de sistema do Postgres (sempre existe) — não declarar aqui.
            // criado_em / alterado_em preenchidos pelo AuditTimestampsInterceptor.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS public.report_runs (
    id                       uuid          NOT NULL PRIMARY KEY,
    empresa_id               uuid,
    usuario_solicitante_id   uuid          NOT NULL,
    report_key               varchar(120)  NOT NULL,
    categoria                smallint      NOT NULL,
    contexto                 smallint      NOT NULL,
    params_json              jsonb         NOT NULL,
    params_hash              char(64)      NOT NULL,
    idempotency_key          varchar(80),
    format                   smallint      NOT NULL,
    status                   smallint      NOT NULL,
    semantic_version         varchar(20)   NOT NULL DEFAULT '1.0',
    artifact_storage_key     varchar(512),
    artifact_size_bytes      bigint,
    artifact_sha256          char(64),
    row_count                bigint,
    warnings_json            jsonb,
    error_message            text,
    error_class              varchar(255),
    tentativas               integer       NOT NULL DEFAULT 0,
    max_tentativas           integer       NOT NULL DEFAULT 3,
    motivo_execucao          varchar(500),
    enqueued_at              timestamptz   NOT NULL,
    started_at               timestamptz,
    finished_at              timestamptz,
    lease_expires_at         timestamptz,
    next_attempt_at          timestamptz,
    expires_at               timestamptz,
    criado_em                timestamptz   NOT NULL,
    alterado_em              timestamptz   NOT NULL,

    -- Status válido (0-5): Pending=0, Running=1, Succeeded=2, Canceled=3, Failed=4, Canceling=5
    CONSTRAINT ck_report_runs_status
        CHECK (status BETWEEN 0 AND 5),

    -- Se Succeeded (2), artefato deve estar preenchido
    CONSTRAINT ck_report_runs_succeeded_artifact
        CHECK (
            status <> 2 OR (
                artifact_storage_key IS NOT NULL
                AND artifact_size_bytes IS NOT NULL
                AND artifact_sha256   IS NOT NULL
            )
        ),

    -- Owner consistente: Tenant tem empresa_id, Admin tem empresa_id nulo
    -- (usuario_solicitante_id distingue o solicitante em ambos os contextos)
    CONSTRAINT ck_report_runs_owner_consistente
        CHECK (
            (contexto = 1 AND empresa_id IS NOT NULL)
            OR
            (contexto = 2 AND empresa_id IS NULL)
        ),

    -- Tentativas dentro do limite (aceita tentativas = max+1 para MarkFailed terminal)
    CONSTRAINT ck_report_runs_tentativas
        CHECK (tentativas >= 0 AND tentativas <= max_tentativas + 1)
);
", suppressTransaction: false);

            // ── Índices CONCURRENTLY (exigem suppressTransaction) ─────────────────────

            // Polling do runner — runs Pending por tenant (round-robin ADR-R04)
            migrationBuilder.Sql(@"
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_report_runs_pending_picker_tenant
    ON public.report_runs (empresa_id, COALESCE(next_attempt_at, enqueued_at), enqueued_at)
    WHERE status = 0 AND contexto = 1;
", suppressTransaction: true);

            // Polling do runner — runs Pending Admin SaaS
            migrationBuilder.Sql(@"
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_report_runs_pending_picker_admin
    ON public.report_runs (usuario_solicitante_id, COALESCE(next_attempt_at, enqueued_at), enqueued_at)
    WHERE status = 0 AND contexto = 2;
", suppressTransaction: true);

            // Watchdog — lease expirado (runs Running com lease vencido)
            migrationBuilder.Sql(@"
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_report_runs_running_lease
    ON public.report_runs (lease_expires_at)
    WHERE status = 1;
", suppressTransaction: true);

            // Listagem ""Meus relatórios"" por usuário
            migrationBuilder.Sql(@"
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_report_runs_user_listing
    ON public.report_runs (empresa_id, usuario_solicitante_id, enqueued_at DESC);
", suppressTransaction: true);

            // Idempotência fraca (paramsHash + janela 5 min)
            migrationBuilder.Sql(@"
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_report_runs_idem_hash
    ON public.report_runs (empresa_id, usuario_solicitante_id, params_hash, enqueued_at DESC);
", suppressTransaction: true);

            // Idempotência forte (idempotency_key UNIQUE por owner)
            // B-14 + §29.1.1: usa COALESCE(empresa_id, usuario_solicitante_id) para cobrir ambos contextos
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS ux_report_runs_idempotency_key
    ON public.report_runs (COALESCE(empresa_id, usuario_solicitante_id), idempotency_key)
    WHERE idempotency_key IS NOT NULL;
", suppressTransaction: true);

            // GC de artefatos expirados
            migrationBuilder.Sql(@"
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_report_runs_expiration
    ON public.report_runs (expires_at)
    WHERE artifact_storage_key IS NOT NULL AND expires_at IS NOT NULL;
", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove índices CONCURRENTLY primeiro, depois a tabela
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ix_report_runs_expiration;", suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ux_report_runs_idempotency_key;", suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ix_report_runs_idem_hash;", suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ix_report_runs_user_listing;", suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ix_report_runs_running_lease;", suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ix_report_runs_pending_picker_admin;", suppressTransaction: true);
            migrationBuilder.Sql(@"DROP INDEX CONCURRENTLY IF EXISTS ix_report_runs_pending_picker_tenant;", suppressTransaction: true);

            migrationBuilder.DropTable(name: "report_runs");
        }
    }
}
