\set ON_ERROR_STOP on

-- F10-B — aplicar manualmente em producao antes do deploy automatico
-- (idempotente, seguro de rodar 2x)

\echo '=== ANTES ==='
SELECT count(*) AS entity_alteracoes_exists
FROM information_schema.tables
WHERE table_name = 'entity_alteracoes';

BEGIN;

-- 1. Cria tabela entity_alteracoes
CREATE TABLE IF NOT EXISTS entity_alteracoes (
    "Id"                uuid PRIMARY KEY,
    "EmpresaId"         uuid NOT NULL,
    "TipoEntidade"      character varying(60) NOT NULL,
    "EntidadeId"        uuid NOT NULL,
    "Acao"              character varying(20) NOT NULL,
    "Campo"             character varying(60),
    "ValorAntigo"       text,
    "ValorNovo"         text,
    "AlteradoPorUserId" uuid,
    "AlteradoPorNome"   character varying(120),
    "Origem"            character varying(20),
    "AlteradoEm"        timestamp with time zone NOT NULL,
    "AlteracoesJson"    text,
    "PiiCriptografado"  text
);

-- 2. Indices
CREATE INDEX IF NOT EXISTS ix_entity_alteracoes_lookup
ON entity_alteracoes ("EmpresaId", "TipoEntidade", "EntidadeId", "AlteradoEm" DESC);

CREATE INDEX IF NOT EXISTS ix_entity_alteracoes_retention
ON entity_alteracoes ("EmpresaId", "AlteradoEm");

-- 3. Registra na EF migration history
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260514055600_F10B_EntityAlteracoes', '9.0.4')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

\echo '=== DEPOIS ==='
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'entity_alteracoes'
ORDER BY ordinal_position;
