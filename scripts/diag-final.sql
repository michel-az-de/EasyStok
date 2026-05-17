\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== CONTAGENS ==='
SELECT 'mobile_orders' AS metric, COUNT(*) AS total FROM mobile_orders WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_orders SEM erp_pedido_id', COUNT(*) FROM mobile_orders WHERE empresa_id = :empresa AND erp_pedido_id IS NULL
UNION ALL SELECT 'mobile_orders SEM erp_venda_id (entregues)', COUNT(*) FROM mobile_orders WHERE empresa_id = :empresa AND "Status"='entregue' AND erp_venda_id IS NULL
UNION ALL SELECT 'mobile_batches', COUNT(*) FROM mobile_batches WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_batches SEM erp_lote_id', COUNT(*) FROM mobile_batches WHERE empresa_id = :empresa AND erp_lote_id IS NULL
UNION ALL SELECT 'mobile_products', COUNT(*) FROM mobile_products WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_products SEM erp_product_id', COUNT(*) FROM mobile_products WHERE empresa_id = :empresa AND erp_product_id IS NULL
UNION ALL SELECT 'mobile_clients', COUNT(*) FROM mobile_clients WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_clients SEM erp_cliente_id', COUNT(*) FROM mobile_clients WHERE empresa_id = :empresa AND erp_cliente_id IS NULL
UNION ALL SELECT 'mobile_cash_entries', COUNT(*) FROM mobile_cash_entries WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_cash_entries SEM erp_movimento_caixa_id', COUNT(*) FROM mobile_cash_entries WHERE empresa_id = :empresa AND erp_movimento_caixa_id IS NULL
UNION ALL SELECT '---', 0
UNION ALL SELECT 'pedidos', COUNT(*) FROM pedidos WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'pedidos por status: aguardando', COUNT(*) FROM pedidos WHERE "EmpresaId" = :empresa AND "Status"='aguardando'
UNION ALL SELECT 'pedidos por status: preparando', COUNT(*) FROM pedidos WHERE "EmpresaId" = :empresa AND "Status"='preparando'
UNION ALL SELECT 'pedidos por status: pronto', COUNT(*) FROM pedidos WHERE "EmpresaId" = :empresa AND "Status"='pronto'
UNION ALL SELECT 'pedidos por status: entregue', COUNT(*) FROM pedidos WHERE "EmpresaId" = :empresa AND "Status"='entregue'
UNION ALL SELECT 'pedido_itens TOTAL', COUNT(*) FROM pedido_itens pi JOIN pedidos p ON p."Id"=pi."PedidoId" WHERE p."EmpresaId" = :empresa
UNION ALL SELECT 'pedido_itens SEM ProdutoId FK', COUNT(*) FROM pedido_itens pi JOIN pedidos p ON p."Id"=pi."PedidoId" WHERE p."EmpresaId" = :empresa AND pi."ProdutoId" IS NULL
UNION ALL SELECT 'pedido_pagamentos', COUNT(*) FROM pedido_pagamentos pp JOIN pedidos p ON p."Id"=pp."PedidoId" WHERE p."EmpresaId" = :empresa
UNION ALL SELECT 'produtos', COUNT(*) FROM produtos WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'clientes', COUNT(*) FROM clientes WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'lotes', COUNT(*) FROM lotes WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'lote_itens', COUNT(*) FROM lote_itens li JOIN lotes l ON l."Id"=li."LoteId" WHERE l."EmpresaId" = :empresa
UNION ALL SELECT 'itens_estoque', COUNT(*) FROM itens_estoque WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'movimentacoes_estoque', COUNT(*) FROM movimentacoes_estoque WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'movimentos_caixa', COUNT(*) FROM movimentos_caixa WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'fechamentos_caixa', COUNT(*) FROM fechamentos_caixa WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'vendas', COUNT(*) FROM vendas WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'itens_venda', COUNT(*) FROM itens_venda iv JOIN vendas v ON v."Id"=iv."VendaId" WHERE v."EmpresaId" = :empresa;

\echo ''
\echo '=== MOBILE ORDERS ==='
SELECT "Id", "Status", "Total", erp_pedido_id IS NOT NULL AS tem_pedido, erp_venda_id IS NOT NULL AS tem_venda, last_operator_name
FROM mobile_orders WHERE empresa_id = :empresa ORDER BY created_at DESC;

\echo ''
\echo '=== PEDIDOS WEB ==='
SELECT p."Id", p."Status", p."ClienteNome", p."MobileOrderId",
  (SELECT COUNT(*) FROM pedido_itens pi WHERE pi."PedidoId" = p."Id") AS qtd_itens,
  (SELECT COUNT(*) FROM pedido_itens pi WHERE pi."PedidoId" = p."Id" AND pi."ProdutoId" IS NOT NULL) AS itens_com_produtoid,
  (SELECT COUNT(*) FROM pedido_pagamentos pp WHERE pp."PedidoId" = p."Id") AS qtd_pagamentos
FROM pedidos p WHERE p."EmpresaId" = :empresa;
