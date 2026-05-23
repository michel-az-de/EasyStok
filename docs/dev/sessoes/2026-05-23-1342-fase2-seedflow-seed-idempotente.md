# Sessao FASE 2 SeedFlow + bug seed idempotente

Data: 2026-05-23 13:42 (UTC-03:00)
Worktree: `.claude/worktrees/wt-api-mongo-tests` (branch `dev/api-mongo-tests`)
Identidade Git: felipe.azevedo@gmail.com / gh michel-az-de
Status final: completo para SeedFlow (FASE 1 + parte da FASE 2) — restantes pendentes na issue #201

## O que foi feito (alem desta sessao gravou FASE 1)

- Validei premissa do "voce e dono do WIP em wt-api-mongo-tests" — confirmado e preservado (R6 honrado)
- Fix SeedFlow (opcao A da matriz #201): tabelas `Perfis`/`Usuarios`/`Empresas`/`NotifTemplates` em PascalCase trocadas para snake_case `perfis`/`usuarios`/`empresas`/`notif_templates`. Adicionalmente `"Nivel" = 0` → `"Nivel" = 'SuperAdmin'` (HasConversion<string>() em NivelAcesso enum)
- Bug REAL descoberto durante validacao do fix: `NotificacoesGlobaisSeed` nao era idempotente — `HasQueryFilter` global (`EmpresaId == CurrentTenantId || IsSuperAdmin`) zerava a leitura de globais (`EmpresaId IS NULL`) durante o seed (CurrentTenantId=Guid.Empty, null != Guid.Empty). Cada restart re-inserva 43 templates + 4 configs canal + 30 rotinas
- Fix: `.IgnoreQueryFilters()` antes do `.Where()` nos 3 seeds (Canal, Templates, Rotinas) em `EasyStock.Api/Data/NotificacoesGlobaisSeed.cs`
- Testes: `SeedFlowIntegrationTests` agora **2/2 verdes** (era 0/2)
- AutoTicket 409 (opcao B): investigacao iniciada — `GlobalExceptionHandler` mapeia `DbUpdateConcurrencyException`/`DbUpdateException(23505)`/`RegraDeDominioVioladaException` para 409; `AutoTicketController` so retorna 200/201. Body do 409 nao capturado pelos testes — abandonado, precisaria instrumentacao adicional. Tempo gasto: ~30 min, sem fix
- Commit/PR/issue:
  - PR [michel-az-de/EasyStok#207](https://github.com/michel-az-de/EasyStok/pull/207) "fix(api/seed): seed de notificacoes globais agora idempotente" — abre contra master
  - Issue [michel-az-de/EasyStok#201](https://github.com/michel-az-de/EasyStok/issues/201) atualizada com comentario referenciando #207

## O que ficou pendente (issue #201)

### Pendencias originais da triagem que ESTA sessao NAO endereçou
- **Mobile AutoTicket 409 (#3, #4 da matriz):** investigacao iniciada mas abandonada — body do 409 precisa instrumentacao (`Output.WriteLine(await resp.Content.ReadAsStringAsync())`) antes de hipotetizar. Hipotese principal segue sendo estado residual + `ICollectionFixture` sem reset entre testes
- **12 skips lentos em `PostgresApiIntegrationTests`:** ~30s/teste = ~6 min desperdiçados. Otimizacao: mover `Skip.IfNot(_seedDemoIndisponivel)` para antes de `CriarFactory()`, ou cachear flag por fixture
- **23/25 falhas Mongo (bug serializer Guid):** PROD protegido por ADR-0001 (`Program.cs:173` `NotSupportedException`). **Decisao estrategica pendente:** corrigir serializer Guid OU `git rm -r EasyStock.Infra.MongoDb*` conforme caminho futuro do ADR. ROI do fix questionavel se ja ha consenso de remover

### Recomendacao para proxima sessao (ordem crescente de risco)
1. (15-30 min, mecanico) Otimizar 12 skips PostgresApi
2. (30-60 min) AutoTicket 409: instrumentar testes para capturar body, depois decidir reset entre tests vs signatures unicas
3. (decisao Felipe) Mongo: corrigir ou remover

## Decisoes tomadas

1. **Felipe confirmou dono do WIP em wt-api-mongo-tests** (R6 nao se aplica — pode tocar; WIP de infra de teste preservado intacto, commitei apenas SeedFlow + seed)
2. **Branch dev/api-mongo-tests mantida** (viola R4 mas pre-existente; renomear adiaria PR)
3. **PR ready (nao draft)** conforme escolha do Felipe
4. **Edit equivocada no master worktree corrigida via `git checkout`** — descobri que worktrees tem working trees independentes; sempre editar no worktree correto

## Commits criados (alem dos da FASE 1)

- `eb9e6172` docs(sessao): handoff Api+Mongo IntegrationTests triagem (#201) — FASE 1, commitado nesta sessao
- `04a84cca` fix(api/seed): seed de notificacoes globais agora idempotente (#201) — FASE 2 parcial

Ambos pushados em `dev/api-mongo-tests`. PR #207 aguarda revisao.

## Branches criadas/deletadas

- Nenhuma criada; commit em `dev/api-mongo-tests` pre-existente

## Estado final

- Master: limpo (snapshot original do CLAUDE.md §5)
- `dev/api-mongo-tests`: 2 commits ahead de master, pushada
- Worktree `wt-api-mongo-tests`: WIP do Felipe preservado intacto (8 arquivos M + 2 untracked — `AssemblyInfo.cs`, `Diagnostics/`)
- PR #207 open
- Issue #201 atualizada

## Proxima acao recomendada

Comecar com **otimizacao dos skips PostgresApi** (item #1 da lista — mecanico, ganha tempo na suite, baixo risco). Em paralelo, **conversar sobre Mongo** (item #3) antes de gastar tempo em investigar serializer.

## Referencias

- PR: https://github.com/michel-az-de/EasyStok/pull/207
- Issue: https://github.com/michel-az-de/EasyStok/issues/201
- Comentario de progresso: https://github.com/michel-az-de/EasyStok/issues/201#issuecomment-4525946816
- Handoff anterior: `docs/dev/sessoes/2026-05-23-1223-api-mongo-triagem.md` (FASE 1)
- ADR Mongo: `docs/adr/0001-mongo-discarded.md`
