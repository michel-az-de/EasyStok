\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== PEDIDOS POR STATUS ==='
SELECT 'mobile' AS origem, "Status", COUNT(*) FROM mobile_orders
WHERE empresa_id = :empresa GROUP BY "Status"
UNION ALL
SELECT 'web', "Status", COUNT(*) FROM pedidos
WHERE "EmpresaId" = :empresa GROUP BY "Status";

\echo ''
\echo '=== MOBILE ORDERS detalhe ==='
SELECT "Id", "Status", "Total", erp_pedido_id, erp_venda_id, last_operator_name, "UpdatedAt"
FROM mobile_orders WHERE empresa_id = :empresa ORDER BY "CreatedAt" DESC;

\echo ''
\echo '=== MOBILE BATCHES detalhe + erp_lote_id ==='
SELECT "Id", "Code", "Lote", "CreatedAt", last_operator_name, erp_lote_id
FROM mobile_batches WHERE empresa_id = :empresa ORDER BY "CreatedAt" DESC;

\echo ''
\echo '=== LOTES web detalhe ==='
SELECT "Id", "Codigo", "Status", "DataProducao", "MobileBatchId"
FROM lotes WHERE "EmpresaId" = :empresa;

\echo ''
\echo '=== PRODUTOS web ==='
SELECT "Id", "Nome", "Status" FROM produtos WHERE "EmpresaId" = :empresa LIMIT 15;

\echo ''
\echo '=== Schema da tabela pedidos (Total column?) ==='
SELECT column_name FROM information_schema.columns
WHERE table_name = 'pedidos' AND column_name ILIKE '%total%' OR column_name ILIKE '%valor%';

\echo ''
\echo '=== PEDIDOS web detalhe ==='
SELECT "Id", "Status", "ClienteNome", "MobileOrderId",
  (SELECT COUNT(*) FROM pedido_itens pi WHERE pi."PedidoId" = pedidos."Id") AS qtd_itens
FROM pedidos WHERE "EmpresaId" = :empresa;

\echo ''
\echo '=== VENDAS web ==='
SELECT "Id", "DataVenda" FROM vendas WHERE "EmpresaId" = :empresa;
