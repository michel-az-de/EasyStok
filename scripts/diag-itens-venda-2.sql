\echo '=== Schema da tabela itens_venda ==='
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'itens_venda' ORDER BY ordinal_position;

\echo ''
\echo '=== Schema da tabela vendas ==='
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'vendas' ORDER BY ordinal_position;

\echo ''
\echo '=== Constraints na itens_venda ==='
SELECT con.conname, pg_get_constraintdef(con.oid)
FROM pg_constraint con
JOIN pg_class rel ON rel.oid = con.conrelid
WHERE rel.relname = 'itens_venda';
