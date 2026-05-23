# Sessao Api+Mongo IntegrationTests — triagem (FASE 1)

Data: 2026-05-23 12:23 (UTC-03:00)
Worktree: `.claude/worktrees/wt-api-mongo-tests` (branch `dev/api-mongo-tests`); sessao iniciada em `.claude/worktrees/amazing-wescoff-8321f7`
Identidade Git: felipe.azevedo@gmail.com / gh michel-az-de
Status final: parcial — FASE 1 (triagem) entregue; FASE 2 (fix) pendente decisao

## O que foi feito

- Inventario inicial conforme CLAUDE.md §0 (master sincronizado @ 344af814, build verde, 12 worktrees ativos)
- Detectado worktree `wt-api-mongo-tests` JA EXISTENTE com 9 arquivos WIP dirty nos projetos exatos da tarefa — Felipe confirmou ser dono do WIP nesta sessao
- Executado `EasyStock.Api.IntegrationTests` via WSL (19m52s) → **27/44 verdes, 4 falhas, 13 skipped**
- Executado `EasyStock.Infra.MongoDb.IntegrationTests` via WSL (13s) → **2/25 verdes, 23 falhas**
- Triagem completa com matriz por classe/causa/severidade
- **Issue criada:** [michel-az-de/EasyStok#201](https://github.com/michel-az-de/EasyStok/issues/201) com matriz completa de 27 falhas + bug REAL de serializacao Guid no Mongo provider

## O que ficou pendente (FASE 2 — aguarda decisao do Felipe)

### Test-data trivial (severidade BAIXA, mecanico)
- `SeedFlowIntegrationTests.ContarBaselineAsync` (`SeedFlowIntegrationTests.cs:101-107`) usa nomes de tabela em PascalCase (`"Perfis"`, `"Usuarios"`, `"Empresas"`, `"NotifTemplates"`) mas as tabelas reais sao snake_case (`perfis`, `usuarios`, `empresas`, `notif_templates`) via `ToTable("...")` em `EntityTypeConfiguration`. **Fix:** trocar nomes; 2 testes voltam.

### Investigar (severidade MEDIA)
- `Mobile.AutoTicketE2ETests.POST_cria_ticket_*` (2 testes) retornam 409 onde esperam 201. **Hipotese:** `[Collection("MobileE2E")]` compartilha fixture `ICollectionFixture<MobileE2EFixture>` sem reset entre testes; estado residual ou constraint de unicidade nao-obvia no backend. `AutoTicketController` so retorna 200/201 explicitos — 409 vem do `AbrirAdminTicketCommand` ou pipeline. Investigar antes de fixar.

### Bug REAL — decisao estrategica (severidade BAIXA em prod, ALTA na suite)
- **Serializacao Guid quebrada em `EasyStock.Infra.MongoDb`:** insert sucede, queries por `Id`/`_id`/`EmpresaId` retornam NULL. Evidencia em `Diagnose_GuidQuery_Approaches` (`BsonSerializerDiagnosticTests.cs`).
- Afeta 23/25 testes da suite Mongo (CRUD basico nao funciona).
- **PROD ja protegido:** ADR-0001 (2026-05-01) bloqueia `Provider=MongoDB` via `NotSupportedException` em `Program.cs:173`.
- **Decisao precisa do Felipe:** (A) corrigir serializer Guid OU (B) executar `git rm -r EasyStock.Infra.MongoDb*` conforme caminho futuro do ADR-0001. ROI do fix questionavel se ja ha consenso de remover.

### Tempo desperdicado em skips visiveis
- 12 testes em `PostgresApiIntegrationTests` pulam por `Skip.IfNot(loginResp.IsSuccessStatusCode, "Seed demo indisponivel")` — `felipe@easystock.com` etc. nao existem com `SEED_DEMO_DATA=false`. Cada teste sobe `WebApplicationFactory` (~30s) antes de pular = **~6 min total**.
- Fix possivel: mover `Skip.IfNot` para antes de `CriarFactory()` (checando uma flag global de `SEED_DEMO_DATA`), OU fazer `Skip.If(_seedDemoIndisponivel)` cacheado por fixture.

## Decisoes tomadas

1. **Felipe confirmou dono do WIP em wt-api-mongo-tests** (R6 nao se aplica — pode-se tocar)
2. **Apenas RODAR e RELATAR — sem corrigir nesta fase** (conforme tarefa)
3. **≥4 falhas + bug real** → criar issue + handoff conforme tarefa
4. **Handoff vai em dev/api-mongo-tests** (junto com WIP, sem inclui-lo no commit; R2 stage por arquivo)
5. **Sem push** (R9 — `git push` exige autorizacao explicita NESTA sessao)

## Commits criados

- `<sha>` (a preencher): docs(sessao): handoff Api+Mongo IntegrationTests triagem (#201)

## Branches criadas/deletadas

- Nenhuma criada/deletada nesta sessao.
- Sessao executou em worktree `.claude/worktrees/amazing-wescoff-8321f7` (branch auto-gerada `dev/amazing-wescoff-8321f7`, viola R4 mas pre-existente, nao usada para commit)
- Handoff commitado em `wt-api-mongo-tests` / `dev/api-mongo-tests` (pre-existente)

## Proxima acao recomendada

Sessao com contexto fresco para FASE 2, na ordem de menor para maior risco:

1. **(2 min)** SeedFlow PascalCase → snake_case. PR pequeno, mecanico.
2. **(15 min)** AutoTicket E2E: investigar fonte do 409 (provavelmente `EmpresaId` unique + `OwnerEmpresaId` constante; ou `AdminTicketService.AbrirAsync` valida algo).
3. **(decisao Felipe)** Mongo: corrigir ou remover? Se manter, o serializer Guid precisa de auditoria completa (Insert vs Find vs Update path) — pode ser custom `BsonSerializerRegistry` ou GuidRepresentation mismatch.
4. **(tempo)** Otimizar skips em `PostgresApiIntegrationTests` (Skip.IfNot antes do CreateFactory).

## Referencias

- Issue: https://github.com/michel-az-de/EasyStok/issues/201
- ADR Mongo: `docs/adr/0001-mongo-discarded.md`
- Sessao anterior (pendencia origem): `docs/dev/sessoes/2026-05-22-0030-estabilizacao-deepdive.md`
- Output completo da suite Mongo: cache local em `tasks/bzguh2h93.output` (transitorio)
- Output completo da suite Api: cache local em `tasks/bne203zx9.output` (transitorio)
