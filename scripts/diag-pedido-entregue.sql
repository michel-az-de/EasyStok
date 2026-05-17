\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== Mobile orders detail ==='
SELECT "Id", "Status", "Total", erp_pedido_id, erp_venda_id, last_operator_name
FROM mobile_orders WHERE empresa_id = :empresa;

\echo ''
\echo '=== Pedidos web detail ==='
SELECT "Id", "Status", "MobileOrderId", "Total_Valor"
FROM pedidos WHERE "EmpresaId" = :empresa;

\echo ''
\echo '=== Venda detail ==='
SELECT "Id", "MobileOrderId"
FROM vendas WHERE "EmpresaId" = :empresa;
