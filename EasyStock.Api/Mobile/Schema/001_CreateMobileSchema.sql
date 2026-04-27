-- Casa da Baba Mobile - Schema inicial
-- Rodar no Azure PostgreSQL do EasyStock
-- Versao: 001
-- Data: 2026-04-23

BEGIN;

-- ===== Produtos =====
CREATE TABLE IF NOT EXISTS mobile_products (
    id              VARCHAR(64) PRIMARY KEY,
    name            VARCHAR(120) NOT NULL,
    emoji           VARCHAR(16),
    category        VARCHAR(16) NOT NULL DEFAULT 'extra',
    unit            VARCHAR(32),
    price           NUMERIC(10, 2),
    stock           INTEGER NOT NULL DEFAULT 0,
    is_custom       BOOLEAN NOT NULL DEFAULT FALSE,
    is_approved     BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_device_id  VARCHAR(64)
);
CREATE INDEX IF NOT EXISTS ix_mobile_products_updated_at ON mobile_products(updated_at);

-- ===== Clientes =====
CREATE TABLE IF NOT EXISTS mobile_clients (
    id              VARCHAR(64) PRIMARY KEY,
    name            VARCHAR(120) NOT NULL,
    apt             VARCHAR(32),
    address         VARCHAR(255),
    phone           VARCHAR(32),
    last_order      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    order_count     INTEGER NOT NULL DEFAULT 0,
    created_at      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_device_id  VARCHAR(64)
);
CREATE INDEX IF NOT EXISTS ix_mobile_clients_updated_at ON mobile_clients(updated_at);
CREATE INDEX IF NOT EXISTS ix_mobile_clients_last_order ON mobile_clients(last_order DESC);

-- ===== Pedidos =====
CREATE TABLE IF NOT EXISTS mobile_orders (
    id                    VARCHAR(64) PRIMARY KEY,
    client_id             VARCHAR(64),
    client_snapshot_name  VARCHAR(120) NOT NULL,
    client_snapshot_ref   VARCHAR(255),
    notes                 TEXT,
    total                 NUMERIC(10, 2) NOT NULL DEFAULT 0,
    status                VARCHAR(16) NOT NULL DEFAULT 'aguardando',
    created_at            TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at            TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_device_id        VARCHAR(64)
);
CREATE INDEX IF NOT EXISTS ix_mobile_orders_status ON mobile_orders(status);
CREATE INDEX IF NOT EXISTS ix_mobile_orders_updated_at ON mobile_orders(updated_at);
CREATE INDEX IF NOT EXISTS ix_mobile_orders_created_at ON mobile_orders(created_at DESC);

CREATE TABLE IF NOT EXISTS mobile_order_items (
    id          BIGSERIAL PRIMARY KEY,
    order_id    VARCHAR(64) NOT NULL REFERENCES mobile_orders(id) ON DELETE CASCADE,
    product_id  VARCHAR(64) NOT NULL,
    name        VARCHAR(120) NOT NULL,
    emoji       VARCHAR(16),
    unit        VARCHAR(32),
    qty         INTEGER NOT NULL DEFAULT 0,
    unit_price  NUMERIC(10, 2) NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_mobile_order_items_order_id ON mobile_order_items(order_id);

-- ===== Lotes de producao =====
CREATE TABLE IF NOT EXISTS mobile_batches (
    id              VARCHAR(64) PRIMARY KEY,
    code            VARCHAR(32) NOT NULL,
    batch_photo     TEXT,
    created_at      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_device_id  VARCHAR(64)
);
CREATE INDEX IF NOT EXISTS ix_mobile_batches_created_at ON mobile_batches(created_at DESC);
CREATE INDEX IF NOT EXISTS ix_mobile_batches_code ON mobile_batches(code);

CREATE TABLE IF NOT EXISTS mobile_batch_items (
    id          BIGSERIAL PRIMARY KEY,
    batch_id    VARCHAR(64) NOT NULL REFERENCES mobile_batches(id) ON DELETE CASCADE,
    product_id  VARCHAR(64) NOT NULL,
    name        VARCHAR(120) NOT NULL,
    emoji       VARCHAR(16),
    unit        VARCHAR(32),
    qty         INTEGER NOT NULL DEFAULT 0,
    photo       TEXT
);
CREATE INDEX IF NOT EXISTS ix_mobile_batch_items_batch_id ON mobile_batch_items(batch_id);

-- ===== Caixa =====
CREATE TABLE IF NOT EXISTS mobile_cash_entries (
    id              VARCHAR(64) PRIMARY KEY,
    type            VARCHAR(16) NOT NULL,
    amount          NUMERIC(10, 2) NOT NULL,
    description     VARCHAR(255) NOT NULL,
    created_at      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_device_id  VARCHAR(64)
);
CREATE INDEX IF NOT EXISTS ix_mobile_cash_entries_created_at ON mobile_cash_entries(created_at DESC);

-- ===== Seed: catalogo inicial do Casa da Baba =====
-- Nao insere se ja existirem produtos (evita sobrescrever estoque atual)
INSERT INTO mobile_products (id, name, emoji, category, unit, price, stock, is_custom)
VALUES
    ('talharim',  'Talharim Fresco',            '🍝', 'massa', '300g',  12.00, 0, FALSE),
    ('lasanha',   'Lasanha Bolonhesa',          '🍲', 'massa', '300g',  30.00, 0, FALSE),
    ('rav-muss',  'Ravioli Mussarela & Limão',  '🍋', 'massa', '300g',  28.00, 0, FALSE),
    ('rav-esp',   'Ravioli Espinafre & Ricota', '🥬', 'massa', '300g',  28.00, 0, FALSE),
    ('pomodoro',  'Molho Pomodoro',             '🍅', 'molho', '250ml', 15.00, 0, FALSE),
    ('bechamel',  'Molho Bechamel',             '🥛', 'molho', '250ml', 15.00, 0, FALSE),
    ('bolonhesa', 'Molho Bolonhesa',            '🥩', 'molho', '250ml', 18.00, 0, FALSE)
ON CONFLICT (id) DO NOTHING;

-- ===== Auditoria: LastOperatorName (adicionado em versão posterior) =====
-- Idempotente: só adiciona se a coluna ainda não existe. Preserva dados.
ALTER TABLE mobile_products     ADD COLUMN IF NOT EXISTS last_operator_name VARCHAR(64);
ALTER TABLE mobile_clients      ADD COLUMN IF NOT EXISTS last_operator_name VARCHAR(64);
ALTER TABLE mobile_orders       ADD COLUMN IF NOT EXISTS last_operator_name VARCHAR(64);
ALTER TABLE mobile_batches      ADD COLUMN IF NOT EXISTS last_operator_name VARCHAR(64);
ALTER TABLE mobile_cash_entries ADD COLUMN IF NOT EXISTS last_operator_name VARCHAR(64);

-- ===== Pedidos: histórico completo + conferência + fact_at (retroativo) =====
-- history: JSONB com array [{ at, op, action, change }] — auditoria detalhada.
-- confirmed_by / confirmed_at: quem revisou ao marcar entregue.
-- fact_at: quando o pedido aconteceu de fato (pode ser < created_at em pedidos retroativos).
ALTER TABLE mobile_orders ADD COLUMN IF NOT EXISTS history          JSONB;
ALTER TABLE mobile_orders ADD COLUMN IF NOT EXISTS confirmed_by     VARCHAR(64);
ALTER TABLE mobile_orders ADD COLUMN IF NOT EXISTS confirmed_at     TIMESTAMP;
ALTER TABLE mobile_orders ADD COLUMN IF NOT EXISTS fact_at          TIMESTAMP;

-- ===== Etiquetas de produção: SKU + peso/validade default + lote do dia =====
-- mobile_products.sku: prefixo do código de barras (slug do id em uppercase, max 8). Gerado no app.
-- mobile_products.default_weight_g / default_validity_days: pré-preenchimento na tela de revisão.
-- mobile_batches.lote: identificador do dia (LOT-YYMMDD). Todas as produções do mesmo dia compartilham.
-- mobile_batch_items.weight_g / validity_days / expires_at: detalhes por linha produzida.
ALTER TABLE mobile_products    ADD COLUMN IF NOT EXISTS sku                   VARCHAR(32);
ALTER TABLE mobile_products    ADD COLUMN IF NOT EXISTS default_weight_g      INTEGER;
ALTER TABLE mobile_products    ADD COLUMN IF NOT EXISTS default_validity_days INTEGER;
ALTER TABLE mobile_batches     ADD COLUMN IF NOT EXISTS lote                  VARCHAR(32);
ALTER TABLE mobile_batch_items ADD COLUMN IF NOT EXISTS weight_g              INTEGER;
ALTER TABLE mobile_batch_items ADD COLUMN IF NOT EXISTS validity_days         INTEGER;
ALTER TABLE mobile_batch_items ADD COLUMN IF NOT EXISTS expires_at            TIMESTAMP;

COMMIT;
