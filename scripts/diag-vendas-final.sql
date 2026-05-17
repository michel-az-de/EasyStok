\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== VENDAS por mobile order ==='
SELECT mo."Id" AS mobile_order_id,
  v."ValorTotal" AS venda_total,
  mo."Total" AS mobile_total,
  (v."ValorTotal" = mo."Total") AS bate,
  (SELECT COUNT(*) FROM itens_venda iv WHERE iv."VendaId" = v."Id") AS qtd_itens
FROM mobile_orders mo
JOIN vendas v ON v."Id" = mo.erp_venda_id
WHERE mo.empresa_id = :empresa AND mo."Status" = 'entregue'
ORDER BY mo."Id" DESC;

\echo ''
\echo '=== MOVIMENTACOES ESTOQUE (saidas das vendas) ==='
SELECT "Natureza", COUNT(*) AS qtd, SUM("Quantidade") AS total_qtd
FROM movimentacoes_estoque WHERE "EmpresaId" = :empresa
GROUP BY "Natureza";

\echo ''
\echo '=== MOVIMENTOS CAIXA ==='
SELECT "Tipo", COUNT(*) AS qtd, SUM("Valor") AS total_valor
FROM movimentos_caixa WHERE "EmpresaId" = :empresa
GROUP BY "Tipo";
