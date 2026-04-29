-- ============================================================
-- Onda 3 — Vendas mobile -> Venda ERP.
--
-- Idempotente: ADD COLUMN IF NOT EXISTS.
--
-- Pedido mobile que vira "entregue" cria uma Venda no ERP. Guardamos o
-- vinculo aqui pra:
--   - Estorno: cancelamento de pedido entregue marca Venda como cancelada.
--   - Idempotencia: re-envio de mutation nao duplica Venda.
--   - Auditoria: gestor ve no painel ERP qual Venda veio do mobile.
-- ============================================================

ALTER TABLE mobile_orders
    ADD COLUMN IF NOT EXISTS erp_venda_id uuid NULL;

-- Index pra lookup reverso (Venda -> Order mobile origem)
CREATE INDEX IF NOT EXISTS ix_mobile_orders_erp_venda_id
    ON mobile_orders(erp_venda_id) WHERE erp_venda_id IS NOT NULL;
