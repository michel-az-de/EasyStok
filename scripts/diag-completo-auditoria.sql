\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== 1. CLIENTE PATRICIA — pedidos diretamente vinculados ==='
SELECT cl."Nome", cl."Id" AS cliente_id,
  (SELECT COUNT(*) FROM pedidos WHERE "ClienteId" = cl."Id") AS pedidos_via_fk,
  (SELECT COUNT(*) FROM pedidos WHERE "ClienteNome" = cl."Nome" AND "EmpresaId" = :empresa) AS pedidos_via_nome
FROM clientes cl WHERE cl."EmpresaId" = :empresa AND cl."Nome" ILIKE '%Patr%';

\echo ''
\echo '=== 2. Pedidos com ClienteId NULL (não linkados) ==='
SELECT "ClienteNome", COUNT(*) AS qtd
FROM pedidos WHERE "EmpresaId" = :empresa AND "ClienteId" IS NULL
GROUP BY "ClienteNome" ORDER BY qtd DESC;

\echo ''
\echo '=== 3. Auditoria — tabelas relacionadas ==='
SELECT table_name FROM information_schema.tables
WHERE table_schema='public' AND (table_name LIKE '%audit%' OR table_name LIKE '%alteracoes%' OR table_name LIKE '%historico%')
ORDER BY table_name;

\echo ''
\echo '=== 4. Auditoria — contagens ==='
SELECT 'clientes_alteracoes' AS tabela, COUNT(*) FROM clientes_alteracoes ca
  JOIN clientes c ON c."Id" = ca."ClienteId" WHERE c."EmpresaId" = :empresa
UNION ALL
SELECT 'movimentacoes_estoque_alteracoes', COUNT(*) FROM movimentacoes_estoque_alteracoes mea
  JOIN movimentacoes_estoque me ON me."Id" = mea."MovimentacaoEstoqueId" WHERE me."EmpresaId" = :empresa
UNION ALL
SELECT 'produtos_alteracoes', COUNT(*) FROM produtos_alteracoes pa
  JOIN produtos p ON p."Id" = pa."ProdutoId" WHERE p."EmpresaId" = :empresa;

\echo ''
\echo '=== 5. ESTOQUE — saldo por produto ==='
SELECT p."Nome" AS produto, ie."ProdutoId", ie."Quantidade_Valor" AS qtd_atual,
  (SELECT SUM(CASE WHEN m."Tipo"='Entrada' THEN m."Quantidade"
                   WHEN m."Tipo"='Saida'   THEN -m."Quantidade" END)
   FROM movimentacoes_estoque m WHERE m."ItemEstoqueId" = ie."Id") AS saldo_movs
FROM itens_estoque ie
JOIN produtos p ON p."Id" = ie."ProdutoId"
WHERE ie."EmpresaId" = :empresa
ORDER BY ie."Quantidade_Valor" ASC NULLS FIRST
LIMIT 30;

\echo ''
\echo '=== 6. CAIXA — pagamentos vs movimentos_caixa ==='
SELECT
  (SELECT COUNT(*) FROM pedido_pagamentos pp
    JOIN pedidos p ON p."Id"=pp."PedidoId" WHERE p."EmpresaId" = :empresa) AS pagamentos,
  (SELECT SUM(pp."Valor") FROM pedido_pagamentos pp
    JOIN pedidos p ON p."Id"=pp."PedidoId" WHERE p."EmpresaId" = :empresa) AS pagamentos_total,
  (SELECT COUNT(*) FROM movimentos_caixa WHERE "EmpresaId" = :empresa AND "Tipo"='entrada') AS movs_entrada,
  (SELECT SUM("Valor") FROM movimentos_caixa WHERE "EmpresaId" = :empresa AND "Tipo"='entrada') AS movs_entrada_total,
  (SELECT COUNT(*) FROM mobile_cash_entries WHERE empresa_id = :empresa) AS mobile_cash_total;

\echo ''
\echo '=== 7. CAIXA — referencias dos movimentos ==='
SELECT
  CASE WHEN "Referencia" LIKE 'pedido-pagamento:%' THEN 'pedido-pagamento'
       WHEN "Referencia" LIKE 'mobile-cash:%' THEN 'mobile-cash'
       WHEN "Referencia" IS NULL THEN 'sem-ref' ELSE 'outra' END AS origem,
  "Tipo", COUNT(*) AS qtd, SUM("Valor") AS total
FROM movimentos_caixa WHERE "EmpresaId" = :empresa
GROUP BY 1, 2 ORDER BY 1, 2;

\echo ''
\echo '=== 8. FECHAMENTOS CAIXA ==='
SELECT COUNT(*) AS fechamentos FROM fechamentos_caixa WHERE "EmpresaId" = :empresa;
