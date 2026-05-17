\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== A. Pedidos com ClienteId NULL ==='
SELECT "Id", "ClienteNome", "Status", "Total", "MobileOrderId", "VendaId"
FROM pedidos WHERE "EmpresaId" = :empresa AND "ClienteId" IS NULL;

\echo ''
\echo '=== B. Pedidos com VendaId NULL (entregues) ==='
SELECT COUNT(*) AS pedidos_sem_venda
FROM pedidos WHERE "EmpresaId" = :empresa AND "Status"='entregue' AND "VendaId" IS NULL;

\echo ''
\echo '=== C. AUDITORIA ==='
SELECT 'cliente_alteracoes' AS tabela, COUNT(*) FROM cliente_alteracoes ca
  JOIN clientes c ON c."Id" = ca."ClienteId" WHERE c."EmpresaId" = :empresa
UNION ALL
SELECT 'produto_alteracoes', COUNT(*) FROM produto_alteracoes WHERE "EmpresaId" = :empresa
UNION ALL
SELECT 'movimentacao_estoque_alteracoes', COUNT(*) FROM movimentacao_estoque_alteracoes
UNION ALL
SELECT 'venda_alteracoes', COUNT(*) FROM venda_alteracoes;

\echo ''
\echo '=== D. Schema venda_alteracoes ==='
SELECT column_name FROM information_schema.columns
WHERE table_name='venda_alteracoes' ORDER BY ordinal_position;

\echo ''
\echo '=== E. SALDO estoque (QuantidadeAtual direto) ==='
SELECT
  ie."CodigoInterno",
  p."Nome" AS produto,
  ie."QuantidadeAtual" AS qtd_atual,
  ie."QuantidadeInicial" AS qtd_inicial,
  (SELECT SUM(CASE WHEN m."Tipo"='Entrada' THEN m."Quantidade"
                   WHEN m."Tipo"='Saida'   THEN -m."Quantidade" END)
   FROM movimentacoes_estoque m WHERE m."ItemEstoqueId" = ie."Id") AS saldo_movs
FROM itens_estoque ie
LEFT JOIN produtos p ON p."Id" = ie."ProdutoId"
WHERE ie."EmpresaId" = :empresa
ORDER BY ie."QuantidadeAtual" ASC NULLS FIRST
LIMIT 25;

\echo ''
\echo '=== F. Itens estoque AUTO-criados pelo F8-I (descobrir gap) ==='
SELECT COUNT(*) AS auto_criados, SUM("QuantidadeAtual") AS soma_qtd
FROM itens_estoque WHERE "EmpresaId" = :empresa AND "CodigoInterno" LIKE 'AUTO-%';

\echo ''
\echo '=== G. Caixa fechamentos + saldo dia ==='
SELECT DATE("CriadoEm") AS dia,
  SUM(CASE WHEN "Tipo"='entrada' THEN "Valor" ELSE 0 END) AS entrada,
  SUM(CASE WHEN "Tipo"='saida' THEN "Valor" ELSE 0 END) AS saida,
  SUM(CASE WHEN "Tipo"='entrada' THEN "Valor" ELSE -"Valor" END) AS saldo
FROM movimentos_caixa WHERE "EmpresaId" = :empresa
GROUP BY DATE("CriadoEm") ORDER BY dia DESC LIMIT 15;
