-- ============================================================
-- Onda 2 — Catálogo unificado: mobile_products <-> Produtos ERP.
--
-- Idempotente: rodar várias vezes é seguro (ADD COLUMN IF NOT EXISTS).
--
-- Cobre:
--   1. erp_product_id (uuid nullable) — FK opcional pro Produto do ERP.
--      Mobile e ERP são silos hoje; este campo permite linkar 1:1.
--      NULL = produto vive só no mobile (custom criado no app, não revisado).
--   2. approved_at + approved_by_user_id — audit trail de quando/por
--      quem foi aprovado/linkado no painel web.
--   3. Index pra lookup eficiente de "produtos custom pendentes" na
--      listagem da empresa (UI /produtos-mobile).
-- ============================================================

ALTER TABLE mobile_products
    ADD COLUMN IF NOT EXISTS erp_product_id        uuid       NULL,
    ADD COLUMN IF NOT EXISTS approved_at           timestamp  NULL,
    ADD COLUMN IF NOT EXISTS approved_by_user_id   uuid       NULL;

-- Listagem rápida de produtos pendentes na empresa
-- (is_custom=true AND is_approved=false AND empresa_id=X).
CREATE INDEX IF NOT EXISTS ix_mobile_products_pending_review
    ON mobile_products(empresa_id, is_custom, is_approved)
    WHERE is_custom = TRUE AND is_approved = FALSE;
