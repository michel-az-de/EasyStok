\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== MOBILE ORDERS detalhe ==='
SELECT "Id", "Status", "Total", erp_pedido_id, erp_venda_id, last_operator_name, updated_at
FROM mobile_orders WHERE empresa_id = :empresa ORDER BY "CreatedAt" DESC;

\echo ''
\echo '=== MOBILE BATCHES detalhe (5 batches, 4 nao linkados!) ==='
SELECT "Id", "Code", lote, "CreatedAt", last_operator_name, erp_lote_id
FROM mobile_batches WHERE empresa_id = :empresa ORDER BY "CreatedAt" DESC;

\echo ''
\echo '=== Pedido linkado + itens ==='
SELECT pi."Id", pi."Nome", pi."Quantidade", pi."PrecoUnitario", pi."ProdutoId"
FROM pedido_itens pi
JOIN pedidos p ON p."Id" = pi."PedidoId"
WHERE p."EmpresaId" = :empresa;

\echo ''
\echo '=== Venda existente (origem da venda?) ==='
SELECT "Id", "ValorTotal_Valor", "MobileOrderId", "DataVenda", "Origem"
FROM vendas WHERE "EmpresaId" = :empresa;

\echo ''
\echo '=== Itens da venda ==='
SELECT iv."Id", iv."Quantidade", iv."ValorUnitario_Valor", iv."ProdutoId"
FROM itens_venda iv
JOIN vendas v ON v."Id" = iv."VendaId"
WHERE v."EmpresaId" = :empresa;
