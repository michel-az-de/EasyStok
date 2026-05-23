# Sessao diagnostico read-only do role RLS em prod

Data: 2026-05-23 ~10:00 (BRT)
Worktree: `.claude/worktrees/magical-leavitt-ec08c0` (branch `dev/magical-leavitt-ec08c0`, mergeada e deletada)
Identidade Git: felipe.azevedo@gmail.com / gh michel-az-de
Status final: completo

## O que foi feito

Diagnostico read-only para resolver a pendencia do handoff `2026-05-22-0030-estabilizacao-deepdive.md` ("ACHADO DE SEGURANCA: se em prod o app conecta como dono e for SUPERUSER, FORCE seria bypassado e RLS estaria inativo").

Operacao:
1. `fly ssh console -a easystok -C "printenv ConnectionStrings__DefaultConnection"` capturou a conn string (Render Postgres, fora do Fly).
2. `psql` rodado do **WSL Ubuntu** (Windows host nao tem psql) com a senha em `$PGPASSWORD` de processo — nao em argv nem em arquivo persistido.
3. SELECTs em `pg_roles`, `pg_class.relrowsecurity / relforcerowsecurity`, `pg_policies`, `__EFMigrationsHistory`.
4. Zero ALTER/CREATE/DROP em prod.

**Verediteo: RLS ATIVO em prod.** `easystok_user` tem `rolsuper=f` e `rolbypassrls=f`; FORCE aplicado em todas as 60 tabelas com RLS habilitado; 60 policies `tenant_isolation` presentes.

**Achado adicional**: 3 tabelas com `EmpresaId` criadas em migrations posteriores a `20260511120000_AddRowLevelSecurity` ficaram fora do snapshot do `information_schema` daquela migration e estao **sem RLS**:
- `notif_web_push_subscriptions` (migration `20260516014449_AddWebPushSubscription`)
- `produtos_composicao` (migration `20260516014645_AddProdutoComposicaoEUnidadeMedida`)
- `produtos_composicao_alteracao` (mesma migration acima)

Para essas 3 tabelas, **so o Global Query Filter EF Core protege**. Mitigacao proposta (mas NAO executada): nova migration que re-roda o mesmo `DO $rls$ ... END` original — e idempotente (`DROP POLICY IF EXISTS` + `CREATE POLICY`).

## O que ficou pendente

- **Migration `ReapplyRowLevelSecurity`** para cobrir as 3 tabelas (e qualquer outra que tenha sido adicionada apos). Risco baixo (so ALTER + CREATE POLICY, nenhuma linha tocada), mas exige validacao em Testcontainers (WSL/Docker) e autorizacao R9 para `fly deploy`.
- **Nota no ADR-0010** sobre o caveat do snapshot `information_schema` — instrui que novas tabelas tenant-aware precisam re-aplicar a migration.
- **ArchitectureTest opcional**: carregar `EasyStockDbContext`, listar entidades com `EmpresaId` mapeadas, falhar se alguma tabela mapeada nao tiver policy `tenant_isolation` no banco. Detecta o gap em CI antes do deploy.

Todas estas pendencias estao explicitas no incidente em `docs/dev/incidentes/2026-05-22-rls-prod-role-status.md` (secao "Proxima acao recomendada").

## Decisoes tomadas

- Mascarar host Render (`dpg-***.ohio-postgres.render.com`) no doc publico, mesmo nao sendo credencial — principio de cautela. Senha nunca aparece em commit.
- Nao executar mitigacao das 3 tabelas nesta sessao: fora do escopo (`Apenas LER. NUNCA executar ALTER, CREATE, DROP em prod.`).

## Commits criados

- `a0e3918d` docs(rls): diagnostico role prod easystok_user - RLS ativo + gap pos-migration (#200)

## Branches criadas/deletadas

- Criada e mergeada via admin-squash: `dev/magical-leavitt-ec08c0` (deletada apos merge)

## Proxima acao recomendada

Sessao curta e focada para escrever a migration `ReapplyRowLevelSecurity`:
1. Copiar o `DO $rls$` da migration `20260511120000_AddRowLevelSecurity.cs`.
2. Validar em WSL com Testcontainers (`dotnet test EasyStock.Infra.Postgre.IntegrationTests`).
3. Dry-run com `dotnet ef migrations script` para inspecionar SQL gerado.
4. Pedir autorizacao R9 para deploy.

Alternativamente, esperar a proxima feature que adicione tabela tenant-aware e fazer junto (acumular gap).

## Referencias

- Incidente: `docs/dev/incidentes/2026-05-22-rls-prod-role-status.md` (saida completa do psql + analise)
- ADR: `docs/adr/0010-rls-postgres-defesa-em-profundidade.md`
- Origem da pendencia: `docs/dev/sessoes/2026-05-22-0030-estabilizacao-deepdive.md`
