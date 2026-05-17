\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== ANTES (vendas atuais e cobertura) ==='
SELECT v."Id", v."ValorTotal",
  (SELECT COUNT(*) FROM itens_venda iv WHERE iv."VendaId" = v."Id") AS qtd_itens,
  mo."Id" AS mobile_order_id,
  (SELECT COUNT(*) FROM mobile_order_items oi WHERE oi.order_id = mo."Id") AS qtd_items_mobile
FROM vendas v
LEFT JOIN mobile_orders mo ON mo.erp_venda_id = v."Id"
WHERE v."EmpresaId" = :empresa
ORDER BY mo."Id" DESC;

\echo ''
\echo '=== WIPE: delete vendas + itens + null erp_venda_id ==='
BEGIN;
-- Apaga itens_venda (FK cascade tambem cuidaria, mas explicito eh melhor)
DELETE FROM itens_venda WHERE "VendaId" IN (
  SELECT "Id" FROM vendas WHERE "EmpresaId" = :empresa
);

-- Null erp_venda_id nos mobile_orders
UPDATE mobile_orders SET erp_venda_id = NULL WHERE empresa_id = :empresa;

-- Delete vendas
DELETE FROM vendas WHERE "EmpresaId" = :empresa;
COMMIT;

\echo ''
\echo '=== DEPOIS ==='
SELECT 'vendas total' AS metric, COUNT(*) FROM vendas WHERE "EmpresaId" = :empresa
UNION ALL
SELECT 'itens_venda total', COUNT(*) FROM itens_venda iv
  JOIN vendas v ON v."Id" = iv."VendaId" WHERE v."EmpresaId" = :empresa
UNION ALL
SELECT 'mobile_orders SEM erp_venda_id (entregues)', COUNT(*)
FROM mobile_orders WHERE empresa_id = :empresa AND "Status"='entregue' AND erp_venda_id IS NULL;
