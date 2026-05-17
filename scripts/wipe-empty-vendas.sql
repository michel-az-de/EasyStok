\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== ANTES ==='
SELECT 'vendas com 0 itens' AS metric, COUNT(*)
FROM vendas v
WHERE v."EmpresaId" = :empresa
  AND NOT EXISTS (SELECT 1 FROM itens_venda iv WHERE iv."VendaId" = v."Id")
UNION ALL
SELECT 'mobile_orders entregues com erp_venda_id', COUNT(*)
FROM mobile_orders WHERE empresa_id = :empresa AND "Status"='entregue' AND erp_venda_id IS NOT NULL;

\echo ''
\echo '=== WIPE ==='
BEGIN;

-- Nullify erp_venda_id em mobile_orders cujas Vendas estao vazias
UPDATE mobile_orders mo
SET erp_venda_id = NULL
WHERE mo.empresa_id = :empresa
  AND mo.erp_venda_id IN (
    SELECT v."Id" FROM vendas v
    WHERE v."EmpresaId" = :empresa
      AND NOT EXISTS (SELECT 1 FROM itens_venda iv WHERE iv."VendaId" = v."Id")
  );

-- Delete vendas vazias
DELETE FROM vendas v
WHERE v."EmpresaId" = :empresa
  AND NOT EXISTS (SELECT 1 FROM itens_venda iv WHERE iv."VendaId" = v."Id");

COMMIT;

\echo ''
\echo '=== DEPOIS ==='
SELECT 'vendas total' AS metric, COUNT(*) FROM vendas WHERE "EmpresaId" = :empresa
UNION ALL
SELECT 'mobile_orders SEM erp_venda_id (entregues)', COUNT(*)
FROM mobile_orders WHERE empresa_id = :empresa AND "Status"='entregue' AND erp_venda_id IS NULL;
