-- ============================================================
-- Onda 8 — Backup automático do localStorage do PWA.
--
-- Idempotente: CREATE TABLE IF NOT EXISTS + indexes IF NOT EXISTS.
--
-- Caso de uso: Felipe perde celular / reinstala APK / faz factory reset.
-- App envia snapshot diariamente. Gestor pode baixar último snapshot e
-- restaurar manualmente pelo Diagnóstico do app (cola JSON em "Restaurar").
--
-- Mantemos só os N mais recentes por device (rotação no upload). Se gestor
-- precisar histórico longo, baixa antes que rotacione.
-- ============================================================

CREATE TABLE IF NOT EXISTS mobile_device_backups (
    "Id"             uuid          PRIMARY KEY,
    device_id        varchar(64)   NOT NULL,
    empresa_id       uuid          NOT NULL,
    snapshot_json    text          NOT NULL,        -- JSON inteiro (cdb-* keys)
    size_bytes       integer       NOT NULL,
    created_at       timestamp     NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    bundle_version   varchar(64)   NULL,
    operator_name    varchar(64)   NULL,
    note             varchar(255)  NULL              -- "auto" ou nota manual
);

-- Listagem por device (painel web)
CREATE INDEX IF NOT EXISTS ix_mobile_device_backups_device
    ON mobile_device_backups(device_id, created_at DESC);

-- Listagem por empresa (admin)
CREATE INDEX IF NOT EXISTS ix_mobile_device_backups_empresa
    ON mobile_device_backups(empresa_id, created_at DESC);
