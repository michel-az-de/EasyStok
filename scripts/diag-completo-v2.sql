\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== A. Pedidos com ClienteId NULL ==='
SELECT "Id", "ClienteNome", "Status", "Total_Valor", "MobileOrderId"
FROM pedidos WHERE "EmpresaId" = :empresa AND "ClienteId" IS NULL;

\echo ''
\echo '=== B. AUDITORIA — contagens corretas ==='
SELECT 'cliente_alteracoes' AS tabela, COUNT(*) FROM cliente_alteracoes ca
  JOIN clientes c ON c."Id" = ca."ClienteId" WHERE c."EmpresaId" = :empresa
UNION ALL
SELECT 'movimentacao_estoque_alteracoes', COUNT(*) FROM movimentacao_estoque_alteracoes mea
  JOIN movimentacoes_estoque me ON me."Id" = mea."MovimentacaoEstoqueId" WHERE me."EmpresaId" = :empresa
UNION ALL
SELECT 'produto_alteracoes', COUNT(*) FROM produto_alteracoes pa
  JOIN produtos p ON p."Id" = pa."ProdutoId" WHERE p."EmpresaId" = :empresa
UNION ALL
SELECT 'venda_alteracoes', COUNT(*) FROM venda_alteracoes va
  JOIN vendas v ON v."Id" = va."VendaId" WHERE v."EmpresaId" = :empresa
UNION ALL
SELECT 'audit_logs (empresa)', COUNT(*) FROM audit_logs WHERE "EmpresaId" = :empresa;

\echo ''
\echo '=== C. Schema produtos_alteracoes ==='
SELECT column_name, data_type FROM information_schema.columns
WHERE table_name='produto_alteracoes' ORDER BY ordinal_position;

\echo ''
\echo '=== D. ESTOQUE — descobrir nome real ==='
SELECT column_name FROM information_schema.columns
WHERE table_name='itens_estoque' AND column_name ILIKE '%qtd%' OR column_name ILIKE '%quanti%';

\echo ''
\echo '=== E. SALDOS — itens_estoque ==='
SELECT p."Nome", ie."ProdutoId",
  COALESCE(ie."QuantidadeAtual_Valor", ie."Quantidade_Valor") AS qtd,
  ie."LojaId"
FROM itens_estoque ie
LEFT JOIN produtos p ON p."Id" = ie."ProdutoId"
WHERE ie."EmpresaId" = :empresa
ORDER BY 3 ASC NULLS FIRST
LIMIT 30;
