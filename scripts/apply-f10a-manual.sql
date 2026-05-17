\set ON_ERROR_STOP on

-- F10-A — aplicar manualmente em produção antes do deploy automático
-- (idempotente, seguro de rodar 2x; replica o que a migration EF faz)

\echo '=== ANTES ==='
SELECT column_name FROM information_schema.columns
WHERE table_name='cliente_alteracoes' AND column_name='EmpresaId';

BEGIN;

-- 1. Adiciona coluna nullable
ALTER TABLE cliente_alteracoes
ADD COLUMN IF NOT EXISTS "EmpresaId" uuid;

-- 2. Backfill
UPDATE cliente_alteracoes ca
SET "EmpresaId" = c."EmpresaId"
FROM clientes c
WHERE c."Id" = ca."ClienteId"
  AND ca."EmpresaId" IS NULL;

-- 3. NOT NULL
ALTER TABLE cliente_alteracoes
ALTER COLUMN "EmpresaId" SET NOT NULL;

-- 4. Drop index antigo
DROP INDEX IF EXISTS "IX_cliente_alteracoes_ClienteId_AlteradoEm";

-- 5. Cria index composto
CREATE INDEX IF NOT EXISTS "IX_cliente_alteracoes_EmpresaId_ClienteId_AlteradoEm"
ON cliente_alteracoes ("EmpresaId", "ClienteId", "AlteradoEm");

-- 6. Insere o registro do migration history pra EF não tentar aplicar de novo
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260514043702_F10A_ClienteAlteracaoEmpresaId', '9.0.4')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

\echo '=== DEPOIS ==='
SELECT column_name, is_nullable FROM information_schema.columns
WHERE table_name='cliente_alteracoes' AND column_name='EmpresaId';

\echo ''
\echo '=== Patricia agora visivel via filter ==='
SELECT "Id", "EmpresaId", "ClienteId", "Campo", "Origem"
FROM cliente_alteracoes
WHERE "ClienteId" = '95da3ff3-c987-413e-8636-3f6520d56a89';
