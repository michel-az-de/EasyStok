# Diagnóstico: status real de RLS em produção (role app)

Data: 2026-05-22
Tipo: diagnóstico read-only (nenhuma mudança em prod)
Origem: pendência aberta no handoff [`2026-05-22-0030-estabilizacao-deepdive.md`](../sessoes/2026-05-22-0030-estabilizacao-deepdive.md) (item "ACHADO DE SEGURANÇA" — se em prod o app conecta como dono e for SUPERUSER, FORCE seria bypassado e RLS estaria inativo).
Relacionado: [ADR-0010](../../adr/0010-rls-postgres-defesa-em-profundidade.md).

## Verediteo

**RLS está ATIVO em produção.** O role do app (`easystok_user`) **não é SUPERUSER e não tem BYPASSRLS**. A migration `AddRowLevelSecurity` aplica `FORCE ROW LEVEL SECURITY` em todas as 60 tabelas tenant-aware existentes na época, e o role do app — apesar de ser o dono das tabelas — respeita as policies por força do FORCE.

Nada a fazer no objetivo principal. **Há, porém, um gap adicional descoberto neste diagnóstico** — três tabelas com `EmpresaId` criadas em migrations posteriores à de RLS não estão protegidas. Detalhado abaixo na seção [Achado adicional](#achado-adicional-3-tabelas-com-empresaid-criadas-após-a-migration-de-rls).

## Como foi verificado

1. Connection string obtida via `fly ssh console -a easystok -C "printenv ConnectionStrings__DefaultConnection"`. Postgres é hospedado no Render (host `dpg-***.ohio-postgres.render.com`), não no Fly — `fly proxy` não foi necessário, conexão direta TLS.
2. `psql` (PostgreSQL 16.14) executado a partir do WSL Ubuntu (host Windows não tem psql instalado). Senha passada por `PGPASSWORD` em variável de ambiente de processo — nunca em argv nem em arquivo persistido.
3. Apenas SELECTs. Nenhum DDL/DML em prod.

### Comandos rodados

```sql
-- (1) Privilégios do role atual
SELECT current_user, current_database(), current_schema();
SELECT rolname, rolsuper, rolcreaterole, rolcreatedb, rolbypassrls, rolinherit
FROM pg_roles WHERE rolname = current_user;

-- (2) Estado RLS por tabela
SELECT count(*) AS total_tables,
       count(*) FILTER (WHERE relrowsecurity)       AS rls_enabled,
       count(*) FILTER (WHERE relforcerowsecurity)  AS rls_forced
FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind = 'r' AND n.nspname = current_schema();

-- (3) Tabelas com EmpresaId mas SEM RLS habilitado
SELECT c.relname FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'EmpresaId'
WHERE c.relkind = 'r' AND n.nspname = current_schema() AND NOT c.relrowsecurity
ORDER BY c.relname;

-- (4) Tabelas com RLS mas SEM FORCE (cenário "dono bypassa")
SELECT c.relname FROM pg_class c
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'EmpresaId'
WHERE c.relkind = 'r' AND n.nspname = current_schema()
  AND c.relrowsecurity AND NOT c.relforcerowsecurity
ORDER BY c.relname;

-- (5) Policy tenant_isolation por tabela
SELECT count(*) FROM pg_policies
WHERE schemaname = current_schema() AND policyname = 'tenant_isolation';

-- (6) Owner de tabelas amostra
SELECT c.relname, pg_get_userbyid(c.relowner) AS owner
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind = 'r' AND n.nspname = current_schema()
  AND c.relname IN ('produtos', 'empresas', 'usuarios')
ORDER BY c.relname;
```

### Saída integral

```
=== Identidade ===
 current_user  | current_database | current_schema
---------------+------------------+----------------
 easystok_user | easystok_qapf    | public

=== Privilegios do role atual ===
    rolname    | rolsuper | rolcreaterole | rolcreatedb | rolbypassrls | rolinherit
---------------+----------+---------------+-------------+--------------+------------
 easystok_user | f        | t             | t           | f            | t

=== Contagem de tabelas (schema public) ===
 total_tables | rls_enabled | rls_forced
--------------+-------------+------------
          131 |          60 |         60

=== Tabelas com EmpresaId mas sem RLS ===
          table_name
-------------------------------
 TenantFeatureFlags
 admin_impersonation_logs
 fatura_contador
 mobile_processed_mutations
 notif_web_push_subscriptions
 produtos_composicao
 produtos_composicao_alteracao

=== Tabelas com RLS mas sem FORCE ===
(0 rows)

=== Policies tenant_isolation ===
 tenant_isolation_policies
---------------------------
                        60

=== Owner de tabelas amostra ===
 relname  |     owner
----------+---------------
 empresas | easystok_user
 produtos | easystok_user
 usuarios | easystok_user
```

## Análise

| Pergunta | Resposta |
|---|---|
| Role do app é SUPERUSER? | **Não** (`rolsuper = f`). |
| Role do app tem BYPASSRLS? | **Não** (`rolbypassrls = f`). |
| Role do app é dono das tabelas? | **Sim** (`pg_get_userbyid(relowner) = easystok_user` em amostra). |
| FORCE ROW LEVEL SECURITY está aplicado? | **Sim em todas as 60** tabelas com RLS habilitado (`rls_enabled = rls_forced = 60`). |
| Policy `tenant_isolation` existe? | **Sim, em todas as 60** tabelas (1 policy por tabela). |
| Existe tabela com RLS sem FORCE? | **Não.** O cenário "dono bypassa silenciosamente" não está acontecendo. |

Como o role não é superuser nem tem bypass, e como FORCE está ativo em todas as tabelas com RLS habilitado, **a defesa em profundidade do ADR-0010 está efetiva em produção**. Uma SQL crua via `FromSqlRaw` sem `SET app.empresa_id` retorna 0 linhas (`NULLIF('','')::uuid` → NULL → comparação UNKNOWN → fail-closed), conforme projetado.

O role tem `rolcreaterole = t` e `rolcreatedb = t` — privilégios que Render concede ao owner do banco — mas nenhum desses flags afeta RLS.

## Achado adicional: 3 tabelas com `EmpresaId` criadas após a migration de RLS

A migration `20260511120000_AddRowLevelSecurity` itera `information_schema.columns` no momento em que é aplicada — é um snapshot. Tabelas criadas em migrations posteriores que adicionam `EmpresaId` **não são automaticamente protegidas**.

Em prod, três tabelas estão nessa condição:

| Tabela | Migration que a criou | Tem EmpresaId? | Tem LojaId? | RLS? |
|---|---|---|---|---|
| `notif_web_push_subscriptions` | `20260516014449_AddWebPushSubscription` | sim (uuid) | — | ❌ |
| `produtos_composicao` | `20260516014645_AddProdutoComposicaoEUnidadeMedida` | sim (uuid) | sim (uuid) | ❌ |
| `produtos_composicao_alteracao` | `20260516014645_AddProdutoComposicaoEUnidadeMedida` | sim (uuid) | sim (uuid) | ❌ |

As 4 outras tabelas com `EmpresaId` mas sem RLS são intencionais (skip list do ADR-0010):
- `TenantFeatureFlags` (toggles globais)
- `admin_impersonation_logs` (auditoria cross-tenant)
- `fatura_contador` (PK composta, INSERT...ON CONFLICT — RLS zerá fallback)
- `mobile_processed_mutations` (prefixo `mobile_*` — isolamento por loja, não empresa)

**Impacto:** as 3 tabelas dependem 100% do Global Query Filter EF Core (primeira camada). Qualquer `FromSqlRaw`, `IgnoreQueryFilters()` ou regressão no `ApplyTenantQueryFilters` que tocar nessas tabelas vaza cross-tenant. É o cenário exato que o ADR-0010 quis evitar — apenas para essas 3.

**Mitigação proposta (NÃO executada):** criar nova migration `YYYYMMDDHHMMSS_ReapplyRowLevelSecurity` que **re-roda o mesmo `DO $rls$ ... END` da migration original**. Como o loop usa `DROP POLICY IF EXISTS tenant_isolation` antes de `CREATE POLICY`, é idempotente para as 60 já protegidas e cobre as 3 novas. Prod-safe (apenas ALTER + CREATE POLICY; nenhuma linha tocada). Validar primeiro em Testcontainers (WSL/Docker), conforme handoff `2026-05-22-0030-estabilizacao-deepdive.md`.

Alternativa mais robusta a longo prazo: adicionar um `ArchitectureTest` que carrega `EasyStockDbContext`, lista entidades com `EmpresaId`, e falha se alguma tabela mapeada não tem policy correspondente no banco — detecção em CI antes do deploy. Fora do escopo deste diagnóstico.

## Restrições aplicadas neste diagnóstico

- R9 (CLAUDE.md): apenas `SELECT`. Zero ALTER/CREATE/DROP em prod. Nenhuma mudança de role, senha ou conexão.
- Connection string nunca foi escrita em commit, log público ou stdout persistente. Os arquivos SQL temporários (`/tmp/check_rls_*.sql` no WSL) foram removidos ao final; eles continham apenas SELECTs, sem credenciais.
- Senha do role mascarada como `***` em qualquer referência neste documento. Host parcialmente mascarado (`dpg-***`).

## Próxima ação recomendada

1. **Nenhuma ação urgente** no escopo principal — RLS ativo conforme projetado.
2. **Considerar** a migration `ReapplyRowLevelSecurity` numa sessão futura focada (deve ser ~5 linhas reaproveitando o SQL existente; risco baixo, mas requer validação em Testcontainers e autorização R9 para `fly deploy`). Trade-off vs. esperar: o gap atual cobre 3 tabelas, e o filtro EF segue ativo para elas — não é incêndio.
3. **Adicionar nota no ADR-0010** sobre o snapshot do `information_schema` e a necessidade de re-aplicar a migration quando novas tabelas tenant-aware forem criadas. Pode ser feito junto com a migration acima ou separadamente.
