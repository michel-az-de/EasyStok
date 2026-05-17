\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== ANTES ==='
SELECT COUNT(*) AS qtd_alteracoes FROM cliente_alteracoes ca
JOIN clientes c ON c."Id" = ca."ClienteId" WHERE c."EmpresaId" = :empresa;

\echo ''
\echo '=== INSERT entries de auditoria pros clientes ja criados via mobile ==='
INSERT INTO cliente_alteracoes ("Id", "ClienteId", "AlteradoPorNome", "Campo", "ValorAntigo", "ValorNovo", "AlteradoEm", "Origem")
SELECT
  gen_random_uuid(),
  c."Id",
  'Sync inicial',
  'criado',
  NULL,
  'Nome=' || c."Nome" || COALESCE('; Telefone=' || NULLIF(c."Telefone", ''), '') || '; via mobile sync',
  c."CriadoEm",
  'mobile'
FROM clientes c
WHERE c."EmpresaId" = :empresa
  AND NOT EXISTS (SELECT 1 FROM cliente_alteracoes ca WHERE ca."ClienteId" = c."Id");

\echo ''
\echo '=== DEPOIS ==='
SELECT c."Nome", COUNT(ca."Id") AS alteracoes
FROM clientes c LEFT JOIN cliente_alteracoes ca ON ca."ClienteId" = c."Id"
WHERE c."EmpresaId" = :empresa
GROUP BY c."Nome" ORDER BY c."Nome";
