-- Diagnóstico completo do estado da Casa da Babá (empresaId=ecc90223...)
-- Compara mobile_* (origem APK) vs entidades web (Pedido/Produto/Cliente/Lote/etc)
-- Mostra onde o F0-F7 falhou a propagar.

\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== CONTAGENS POR TABELA ==='
SELECT 'mobile_orders'      AS tabela, COUNT(*) AS total,
       SUM(CASE WHEN erp_pedido_id IS NULL THEN 1 ELSE 0 END) AS sem_erp_link,
       SUM(CASE WHEN erp_venda_id  IS NULL THEN 1 ELSE 0 END) AS sem_venda_link
FROM mobile_orders WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_products', COUNT(*),
       SUM(CASE WHEN erp_product_id IS NULL THEN 1 ELSE 0 END), 0
FROM mobile_products WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_clients', COUNT(*),
       SUM(CASE WHEN erp_cliente_id IS NULL THEN 1 ELSE 0 END), 0
FROM mobile_clients WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_batches', COUNT(*),
       SUM(CASE WHEN erp_lote_id IS NULL THEN 1 ELSE 0 END), 0
FROM mobile_batches WHERE empresa_id = :empresa
UNION ALL SELECT 'mobile_cash_entries', COUNT(*),
       SUM(CASE WHEN erp_movimento_caixa_id IS NULL THEN 1 ELSE 0 END), 0
FROM mobile_cash_entries WHERE empresa_id = :empresa
UNION ALL SELECT 'pedidos', COUNT(*),
       SUM(CASE WHEN "MobileOrderId" IS NULL OR "MobileOrderId" = '' THEN 1 ELSE 0 END),
       SUM(CASE WHEN "VendaId" IS NULL THEN 1 ELSE 0 END)
FROM pedidos WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'produtos', COUNT(*), 0, 0 FROM produtos WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'clientes', COUNT(*), 0, 0 FROM clientes WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'lotes',    COUNT(*),
       SUM(CASE WHEN "MobileBatchId" IS NULL OR "MobileBatchId" = '' THEN 1 ELSE 0 END), 0
FROM lotes WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'lote_itens', COUNT(*), 0, 0
FROM lote_itens WHERE "LoteId" IN (SELECT "Id" FROM lotes WHERE "EmpresaId" = :empresa)
UNION ALL SELECT 'vendas',   COUNT(*), 0, 0 FROM vendas WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'itens_venda', COUNT(*), 0, 0
FROM itens_venda WHERE "VendaId" IN (SELECT "Id" FROM vendas WHERE "EmpresaId" = :empresa)
UNION ALL SELECT 'pedido_pagamentos', COUNT(*), 0, 0
FROM pedido_pagamentos WHERE "PedidoId" IN (SELECT "Id" FROM pedidos WHERE "EmpresaId" = :empresa)
UNION ALL SELECT 'itens_estoque', COUNT(*), 0, 0 FROM itens_estoque WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'movimentacoes_estoque', COUNT(*), 0, 0 FROM movimentacoes_estoque WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'movimentos_caixa', COUNT(*), 0, 0 FROM movimentos_caixa WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'fechamentos_caixa', COUNT(*), 0, 0 FROM fechamentos_caixa WHERE "EmpresaId" = :empresa
ORDER BY tabela;

\echo ''
\echo '=== PEDIDOS POR STATUS ==='
SELECT 'mobile' AS origem, status, COUNT(*) FROM mobile_orders WHERE empresa_id = :empresa GROUP BY status
UNION ALL
SELECT 'web', "Status", COUNT(*) FROM pedidos WHERE "EmpresaId" = :empresa GROUP BY "Status"
ORDER BY origem, 2;

\echo ''
\echo '=== MOBILE ORDERS sem Pedido linkado ==='
SELECT "Id", status, total, created_at, erp_pedido_id, erp_venda_id, last_operator_name
FROM mobile_orders
WHERE empresa_id = :empresa AND erp_pedido_id IS NULL
LIMIT 20;

\echo ''
\echo '=== Pedidos com pagamento vs sem (entregues) ==='
SELECT p."Status",
       COUNT(*) AS total_pedidos,
       SUM(CASE WHEN EXISTS (SELECT 1 FROM pedido_pagamentos pp WHERE pp."PedidoId" = p."Id") THEN 1 ELSE 0 END) AS com_pagamento,
       SUM(CASE WHEN NOT EXISTS (SELECT 1 FROM pedido_pagamentos pp WHERE pp."PedidoId" = p."Id") THEN 1 ELSE 0 END) AS sem_pagamento
FROM pedidos p
WHERE p."EmpresaId" = :empresa
GROUP BY p."Status";

\echo ''
\echo '=== LOTES + ITENS_ESTOQUE: F2 cria Lote mas adiciona ItemEstoque? ==='
SELECT l."Codigo", l.status, l."DataProducao",
       (SELECT COUNT(*) FROM lote_itens li WHERE li."LoteId" = l."Id") AS itens_lote,
       (SELECT COUNT(*) FROM itens_estoque ie
         WHERE ie."EmpresaId" = l."EmpresaId"
           AND ie."LojaId" = l."LojaId"
           AND ie."CriadoEm" >= l."CriadoEm" - INTERVAL '1 hour'
           AND ie."CriadoEm" <= l."CriadoEm" + INTERVAL '1 hour') AS itens_estoque_no_horario
FROM lotes l
WHERE l."EmpresaId" = :empresa
ORDER BY l."DataProducao" DESC
LIMIT 10;

\echo ''
\echo '=== Movimentações estoque (deveriam ser geradas em "entregue") ==='
SELECT m."Tipo", COUNT(*) AS qtd, SUM(m."Quantidade") AS total_unidades
FROM movimentacoes_estoque m
WHERE m."EmpresaId" = :empresa
GROUP BY m."Tipo";

\echo ''
\echo '=== Movimentos caixa por tipo + origem ==='
SELECT "Tipo", "Origem", COUNT(*), SUM("Valor") AS total
FROM movimentos_caixa
WHERE "EmpresaId" = :empresa
GROUP BY "Tipo", "Origem"
ORDER BY 1, 2;

\echo ''
\echo '=== Snapshot RECENTE mobile_orders (10 mais novos) ==='
SELECT "Id", status, total, last_operator_name, created_at,
       erp_pedido_id IS NOT NULL AS tem_pedido_web,
       erp_venda_id IS NOT NULL AS tem_venda
FROM mobile_orders
WHERE empresa_id = :empresa
ORDER BY created_at DESC LIMIT 10;
