-- ============================================================
-- Onda 1 — Pareamento de dispositivos + multi-tenant.
--
-- Idempotente: usa IF NOT EXISTS pra colunas e CREATE TABLE IF NOT EXISTS.
-- Pode rodar varias vezes sem efeito colateral.
--
-- Cobre:
--   1. Tabela mobile_devices (auth/pareamento)
--   2. Colunas empresa_id / loja_id em mobile_products, mobile_clients,
--      mobile_orders, mobile_batches, mobile_cash_entries (nullable
--      pra compat com registros pre-Onda-1).
--   3. Indexes pra lookup eficiente.
-- ============================================================

-- 1) Tabela de devices pareados.
CREATE TABLE IF NOT EXISTS mobile_devices (
    "Id"                    varchar(64)  PRIMARY KEY,
    api_key                 varchar(64)  NOT NULL,
    empresa_id              uuid         NOT NULL,
    loja_id                 uuid         NOT NULL,
    paired_by_user_id       uuid         NULL,
    "Label"                 varchar(120) NULL,
    default_operator_name   varchar(64)  NULL,
    pairing_code            varchar(16)  NULL,
    pairing_expires_at      timestamp    NULL,
    paired_at               timestamp    NULL,
    last_seen_at            timestamp    NULL,
    last_seen_ip            varchar(64)  NULL,
    revoked                 boolean      NOT NULL DEFAULT FALSE,
    revoked_at              timestamp    NULL,
    revoked_by_user_id      uuid         NULL,
    created_at              timestamp    NOT NULL DEFAULT (now() AT TIME ZONE 'utc'),
    updated_at              timestamp    NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);

-- Lookup pelo api_key (hot path do middleware MobileApiKey)
CREATE UNIQUE INDEX IF NOT EXISTS ix_mobile_devices_api_key
    ON mobile_devices(api_key);

-- Lookup pelo pairing_code (POST /api/mobile/devices/pair)
CREATE INDEX IF NOT EXISTS ix_mobile_devices_pairing_code
    ON mobile_devices(pairing_code);

-- Listagem de devices da empresa no painel /dispositivos
CREATE INDEX IF NOT EXISTS ix_mobile_devices_empresa_id
    ON mobile_devices(empresa_id);

-- 2) Multi-tenant: empresa_id + loja_id em todas mobile_*.
-- ADD COLUMN IF NOT EXISTS funciona em PostgreSQL >= 9.6.
ALTER TABLE mobile_products    ADD COLUMN IF NOT EXISTS empresa_id uuid NULL;
ALTER TABLE mobile_products    ADD COLUMN IF NOT EXISTS loja_id    uuid NULL;
ALTER TABLE mobile_clients     ADD COLUMN IF NOT EXISTS empresa_id uuid NULL;
ALTER TABLE mobile_clients     ADD COLUMN IF NOT EXISTS loja_id    uuid NULL;
ALTER TABLE mobile_orders      ADD COLUMN IF NOT EXISTS empresa_id uuid NULL;
ALTER TABLE mobile_orders      ADD COLUMN IF NOT EXISTS loja_id    uuid NULL;
ALTER TABLE mobile_batches     ADD COLUMN IF NOT EXISTS empresa_id uuid NULL;
ALTER TABLE mobile_batches     ADD COLUMN IF NOT EXISTS loja_id    uuid NULL;
ALTER TABLE mobile_cash_entries ADD COLUMN IF NOT EXISTS empresa_id uuid NULL;
ALTER TABLE mobile_cash_entries ADD COLUMN IF NOT EXISTS loja_id    uuid NULL;

-- Indexes pra scoping no pull (por empresa/loja + updated_at).
CREATE INDEX IF NOT EXISTS ix_mobile_products_loja_updated
    ON mobile_products(loja_id, updated_at);
CREATE INDEX IF NOT EXISTS ix_mobile_clients_loja_updated
    ON mobile_clients(loja_id, updated_at);
CREATE INDEX IF NOT EXISTS ix_mobile_orders_loja_updated
    ON mobile_orders(loja_id, updated_at);
CREATE INDEX IF NOT EXISTS ix_mobile_batches_loja_created
    ON mobile_batches(loja_id, created_at);
CREATE INDEX IF NOT EXISTS ix_mobile_cash_entries_loja_created
    ON mobile_cash_entries(loja_id, created_at);
