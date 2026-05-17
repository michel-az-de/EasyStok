-- Casa da Baba Mobile - Produção por peso + agendamento de pedidos
-- Versao: 002
-- Data: 2026-05-11
--
-- Frente 1: rastreio de gramagem por etiqueta de produção (BatchItem.WeightGrams)
--           + unidade padrão por produto (Product.DefaultUnit/DefaultGrams).
-- Frente 2: data + janela de entrega no pedido (Order.ScheduledFor/ScheduledWindow).
--
-- Todas as colunas são NULLABLE — compat total com registros antigos.

BEGIN;

-- ===== Frente 1: produção com gramagem rastreável =====
ALTER TABLE mobile_batch_items
    ADD COLUMN IF NOT EXISTS weight_grams INTEGER NULL;

ALTER TABLE mobile_products
    ADD COLUMN IF NOT EXISTS default_unit  VARCHAR(16) NULL,
    ADD COLUMN IF NOT EXISTS default_grams INTEGER NULL;

-- Seed: massas do catálogo inicial já são "300g" no Unit, então default
-- é coerente com o cardápio atual da Casa da Baba.
UPDATE mobile_products
   SET default_unit = 'gramas', default_grams = 300
 WHERE category = 'massa' AND default_unit IS NULL;

UPDATE mobile_products
   SET default_unit = 'gramas', default_grams = 250
 WHERE category = 'molho' AND default_unit IS NULL;

-- ===== Frente 2: agendamento de pedidos =====
ALTER TABLE mobile_orders
    ADD COLUMN IF NOT EXISTS scheduled_for    DATE NULL,
    ADD COLUMN IF NOT EXISTS scheduled_window VARCHAR(16) NULL;

-- Índice para "encomendas de hoje" — consulta esperada: WHERE scheduled_for = :date.
CREATE INDEX IF NOT EXISTS ix_mobile_orders_scheduled_for
    ON mobile_orders(scheduled_for) WHERE scheduled_for IS NOT NULL;

COMMIT;
