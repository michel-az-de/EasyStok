\set empresa '\'ecc90223-24f8-4689-bed6-336a57bb4f21\''

\echo '=== Garantir Usuario "Sistema Mobile Sync" pra empresa ==='
-- Cria se nao existe; idempotente via ON CONFLICT no email unico
INSERT INTO usuarios ("Id", "Nome", "Email", "SenhaHash", "Ativo", "FailedLoginAttempts",
  "TemaPreferido", "EmailConfirmado", "CriadoEm", "AlteradoEm")
VALUES (
  gen_random_uuid(),
  'Sistema Mobile Sync',
  'mobile-sync+' || replace('ecc90223-24f8-4689-bed6-336a57bb4f21', '-', '') || '@system.local',
  'DISABLED_NO_LOGIN',
  false, 0, 'auto', false, NOW(), NOW())
ON CONFLICT ("Email") DO NOTHING;

INSERT INTO usuarios_empresas ("Id", "UsuarioId", "EmpresaId", "Ativo", "CriadoEm")
SELECT gen_random_uuid(), u."Id", :empresa, true, NOW()
FROM usuarios u
WHERE u."Email" = 'mobile-sync+' || replace('ecc90223-24f8-4689-bed6-336a57bb4f21', '-', '') || '@system.local'
  AND NOT EXISTS (
    SELECT 1 FROM usuarios_empresas ue
    WHERE ue."UsuarioId" = u."Id" AND ue."EmpresaId" = :empresa
  );

\echo ''
\echo '=== ANTES ==='
SELECT 'produto_alteracoes' AS tabela, COUNT(*) FROM produto_alteracoes WHERE "EmpresaId" = :empresa
UNION ALL
SELECT 'movimentacao_estoque_alteracoes', COUNT(*) FROM movimentacao_estoque_alteracoes WHERE "EmpresaId" = :empresa;

\echo ''
\echo '=== INSERT auditoria pros 59 produtos ja criados ==='
INSERT INTO produto_alteracoes ("Id", "EmpresaId", "ProdutoId", "UsuarioId", "Acao", "AlteracoesJson", "Motivo", "Observacao", "AlteradoEm")
SELECT
  gen_random_uuid(),
  p."EmpresaId",
  p."Id",
  (SELECT "Id" FROM usuarios WHERE "Email" = 'mobile-sync+' || replace('ecc90223-24f8-4689-bed6-336a57bb4f21', '-', '') || '@system.local'),
  'cadastrado',
  NULL,
  'Sync mobile (retroativo)',
  'Produto criado/linkado via mobile sync inicial · Nome=' || p."Nome",
  p."CriadoEm"
FROM produtos p
WHERE p."EmpresaId" = :empresa
  AND NOT EXISTS (SELECT 1 FROM produto_alteracoes pa WHERE pa."ProdutoId" = p."Id");

\echo ''
\echo '=== INSERT auditoria pras 83 movimentacoes ja criadas ==='
INSERT INTO movimentacao_estoque_alteracoes ("Id", "EmpresaId", "MovimentacaoEstoqueId", "UsuarioId",
  "NomeUsuario", "EmailUsuario", "Acao", "Motivo", "Observacao", "AlteracoesJson", "Ip", "UserAgent", "AlteradoEm")
SELECT
  gen_random_uuid(),
  m."EmpresaId",
  m."Id",
  (SELECT "Id" FROM usuarios WHERE "Email" = 'mobile-sync+' || replace('ecc90223-24f8-4689-bed6-336a57bb4f21', '-', '') || '@system.local'),
  'Sistema Mobile Sync',
  NULL,
  'criada',
  'Sync mobile (retroativo) · ' || m."Natureza",
  m."Descricao",
  NULL, NULL,
  'doc=' || COALESCE(m."DocumentoReferencia", '-'),
  m."CriadoEm"
FROM movimentacoes_estoque m
WHERE m."EmpresaId" = :empresa
  AND NOT EXISTS (SELECT 1 FROM movimentacao_estoque_alteracoes mea WHERE mea."MovimentacaoEstoqueId" = m."Id");

\echo ''
\echo '=== DEPOIS ==='
SELECT 'produto_alteracoes' AS tabela, COUNT(*) FROM produto_alteracoes WHERE "EmpresaId" = :empresa
UNION ALL
SELECT 'movimentacao_estoque_alteracoes', COUNT(*) FROM movimentacao_estoque_alteracoes WHERE "EmpresaId" = :empresa;
