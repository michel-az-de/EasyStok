# Sessao otimizacao 12 skips lentos PostgresApiIntegrationTests

Data: 2026-05-23 14:57 (UTC-03:00)
Worktree: `.claude/worktrees/wt-postgres-api-skips` (branch `wt-postgres-api-skips`)
Identidade Git: felipe.azevedo@gmail.com / gh michel-az-de
Status final: completo, aguardando autorizacao R9 para push + PR + comentario na issue

## O que foi feito

- Analisei `EasyStock.Api.IntegrationTests/PostgresApiIntegrationTests.cs` e confirmei a hipotese do prompt: 12 testes (`Login_admin/gerente/operador` + `Endpoint_com_token_admin` x9 InlineData) subiam `WebApplicationFactory<Program>` antes de chegar no `Skip.IfNot(loginResp.IsSuccessStatusCode, "Seed demo indisponivel")`. Cada teste pagava ~30s no path Testcontainers so para descobrir que ia pular.
- Implementei cache classe-scoped com `private static bool? _seedDemoDisponivelCache` + `SemaphoreSlim` para thread-safety. Adicionei helper `SeedDemoDisponivelAsync()` que popula o cache na primeira chamada (cria factory, tenta login admin) e retorna a flag nas chamadas seguintes.
- Movi `Skip.IfNot(await SeedDemoDisponivelAsync(), ...)` para antes de `CriarFactory()` em todos os 12 testes que dependem do seed demo. Removi o `Skip.IfNot` redundante pos-login no `Login_admin/gerente/operador` (a checagem agora acontece cedo, antes da factory).
- `ClienteAutenticadoAdminAsync` mantem o `Skip.IfNot` interno como backstop defensivo (caso o cache fique stale, vira skip em vez de fail).

### Validacao empirica

Inicialmente tentei rodar a suite via Testcontainers (path padrao). Falhou: 23/23 com `IEmailConfirmationTokenRepository` nao registrado. Causa: master sem o WIP do Felipe em `wt-api-mongo-tests` nao tem as env vars (`ConnectionStrings__DefaultConnection`, `Database__Provider=PostgreSql`, etc.) setadas antes de `CreateBuilder` -> `ResolveDatabaseProviderAsync` timeout=3s -> SQLite fallback -> DI quebra. Esperado, R6 honrado (nao mexi no WIP).

Workaround: subi Postgres dedicado via Docker e usei o path `EASYSTOCK_IT_PG` (linhas 48-61 do arquivo) que ja seta as env vars necessarias. Rodei o mesmo filtro `FullyQualifiedName~PostgresApiIntegrationTests` em ambos os worktrees.

| Metrica | Antes (master, sem fix) | Depois (com cache) | Delta |
|---|---|---|---|
| Test execution | 87.3s (1m27s) | **44.7s** | **-48.6%** (-42.5s) |
| Wall time (build+test) | 281.4s | 125.6s | -55.4% |
| Total tests | 23 | 23 | igual |
| Passed | 11 | 11 | igual |
| Skipped | 12 | 12 | igual |
| Failed | 0 | 0 | igual |

Extrapolacao para Testcontainers normal (skip ~30s/teste vs ~7s aqui no path EASYSTOCK_IT_PG): economia esperada ~5min nos 12 skips, ~75% de reducao.

## O que ficou pendente

- **Push da branch e abertura do PR**: aguardando autorizacao R9 explicita do Felipe nesta sessao.
- **Comentario na issue #201** referenciando o PR e o tempo economizado.
- **Outras pendencias da issue #201 (NAO desta sessao)**:
  - AutoTicket 409 (#3, #4 da matriz original) — precisa instrumentacao
  - Mongo Guid serializer — decisao estrategica pendente do Felipe
- **Conflito futuro previsivel**: quando Felipe commitar a WIP em `wt-api-mongo-tests` (12 linhas em `PostgresApiIntegrationTests.cs` — senha non-default + 5 env vars), vai conflitar com este fix. Conflito sera trivial: os blocos sao independentes (WIP modifica `InitializeAsync`; cache modifica trecho mais abaixo da classe).

## Decisoes tomadas

1. **Estrategia (a) static cache, nao IClassFixture**: Felipe OK'd. IClassFixture daria ganho maior (~9.5min -> ~30s no total da classe, porque compartilharia container Postgres + factory entre testes), mas exigiria refatoracao maior. Static cache entrega ganho de 48.6% no path testado, ~75% extrapolado pro Testcontainers, sem mudar a semantica de isolamento por teste.
2. **`ClienteAutenticadoAdminAsync` mantem Skip interno**: redundante, mas defensivo. Sem custo.
3. **Validacao via `EASYSTOCK_IT_PG`, nao Testcontainers**: o path Testcontainers do master atual quebra com DI antes de chegar no Skip — sem WIP do Felipe em `wt-api-mongo-tests`. O path `EASYSTOCK_IT_PG` ja tem env vars setadas, prova a fix sem tocar no WIP do Felipe (R6 honrado).
4. **PR contra master**, nao rebase em cima de `dev/api-mongo-tests`: fix e independente, e quando o WIP do Felipe for commitado, o merge trivial resolve o conflito naquele momento.

## Commits criados

- `8ef6c325` test(api/integration): cachear seed demo check em PostgresApiIntegrationTests
  - Husky pre-commit rodou `rotulagem-architecture-tests` -> 1/1 verde
  - 1 file changed, 44 insertions(+), 9 deletions(-)

## Branches criadas/deletadas

- Criada: `wt-postgres-api-skips` (1 ahead de master)
- Worktree criado: `.claude/worktrees/wt-postgres-api-skips/`
- Nao deletei nada

## Proxima acao recomendada

1. **Push + PR**: `git push -u origin wt-postgres-api-skips` + `gh pr create` contra master (precisa autorizacao R9 explicita).
2. **Comentar na issue #201**: linkar PR, mencionar tempo economizado, marcar item "12 skips lentos PostgresApi" como em revisao.
3. **Limpeza worktree apos merge**: `git worktree remove .claude/worktrees/wt-postgres-api-skips` + `git branch -d wt-postgres-api-skips` (apos merge).

## Referencias

- Issue: https://github.com/michel-az-de/EasyStok/issues/201
- PR anterior do mesmo issue: https://github.com/michel-az-de/EasyStok/pull/207 (SeedFlow + bug seed idempotente, mergeado)
- Handoff anterior: `docs/dev/sessoes/2026-05-23-1342-fase2-seedflow-seed-idempotente.md`
- Triagem original: comentario em https://github.com/michel-az-de/EasyStok/issues/201#issuecomment-4525946816
- Arquivo modificado: `EasyStock.Api.IntegrationTests/PostgresApiIntegrationTests.cs:35-41,145-171,205-296`
