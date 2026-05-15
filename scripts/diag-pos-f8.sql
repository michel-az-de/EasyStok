\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== CONTAGENS pos-F8 ==='
SELECT 'mobile_batches sem erp_lote' AS metric, COUNT(*) FROM mobile_batches
  WHERE empresa_id = :empresa AND erp_lote_id IS NULL
UNION ALL SELECT 'lotes total', COUNT(*) FROM lotes WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'lote_itens total', COUNT(*) FROM lote_itens
  WHERE "LoteId" IN (SELECT "Id" FROM lotes WHERE "EmpresaId" = :empresa)
UNION ALL SELECT 'itens_estoque total', COUNT(*) FROM itens_estoque WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'movimentacoes_estoque', COUNT(*) FROM movimentacoes_estoque WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'movimentos_caixa', COUNT(*) FROM movimentos_caixa WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'pedidos', COUNT(*) FROM pedidos WHERE "EmpresaId" = :empresa
UNION ALL SELECT 'pedido_itens com produtoid', COUNT(*) FROM pedido_itens pi
  JOIN pedidos p ON p."Id" = pi."PedidoId" WHERE p."EmpresaId" = :empresa AND pi."ProdutoId" IS NOT NULL
UNION ALL SELECT 'pedido_pagamentos', COUNT(*) FROM pedido_pagamentos pp
  JOIN pedidos p ON p."Id" = pp."PedidoId" WHERE p."EmpresaId" = :empresa
UNION ALL SELECT 'vendas', COUNT(*) FROM vendas WHERE "EmpresaId" = :empresa;
