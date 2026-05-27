# Auditoria E2E — EasyStock.Api

**Data:** 2026-05-27
**Autor:** Felipe Azevedo (orquestracao) + Claude Opus 4.7 (analise estatica)
**Base de analise:** `master` HEAD `d030e837` (com `72f59d3a` validado como diff irrelevante para o escopo)
**Escopo:** projeto `EasyStock.Api` (controllers + DI + middlewares + bg services + auth + observability) — sem descer em `Application`/`Domain`.
**Metodo:** 3 agentes Explore em paralelo + validacao via grep/leitura direta + recalibracao de achados.

> **Comentario sobre encoding:** este arquivo evita caracteres acentuados em alguns trechos para garantir compatibilidade UTF-8/BOM em qualquer leitor. Conteudo de codigo e file links preservados literalmente.

---

## 1. Sumario executivo

### Numeros

| Severidade | Achados | Esforco estimado |
|---|---|---|
| **P0 — Seguranca / data loss / build** | 3 | 3 PRs |
| **P1 — Bugs / contratos** | 4 | 4 PRs |
| **P2 — Manutencao / SOLID / clean code** | 14 | 11 PRs |
| **P3 — Cosmetico / doc / style** | 10 | 5 PRs |
| **Total** | **31 achados** | **21 TASK-EZ-CR-*** |

### Highlights

- **Build do branch atual `feat/task-ez-agend-001-listar-janelas` esta quebrado** (referencia fantasma a `EasyStock.Application.UseCases.Storefront.Checkout.*` que nao existe). Sessao paralela em curso — nao tocar, alertar dono.
- **Build do MASTER tambem esta quebrado** (descoberto durante esta auditoria): commit `92c9b10b feat(TASK-EZ-PEDIDOS-001)` adicionou `PedidosClienteControllerTests.cs` usando `NSubstitute` mas esqueceu de adicionar o package ao csproj. **6 erros CS0246 em master atualmente.** Ver ACHADO-31.
- **Vazamento sistemico de excecoes** via `BadRequest(ex.Message)` em **137 lugares em 37 controllers** — bypassa o `GlobalExceptionHandler` que existe e esta correto.
- **293+ action methods sem `CancellationToken`** — viola ADR-0013, desperdica thread pool e segura locks de DB em requests cancelados.
- **2 falsos positivos rejeitados** apos validacao direta no codigo (Program.cs SQLite fallback impl tem fail-fast; GlobalExceptionHandler nao vaza stack em prod).
- **2 P1 rebaixados para P3** apos validacao (`MetricsService` cardinalidade explosiva nao acontece porque tem 0 callers — e codigo morto; `CobrancaAssinaturaJob` usa `pg_try_advisory_lock` corretamente em prod).

---

## 2. Inventario do alvo

Estrutura do `EasyStock.Api` no master:

| Pasta | Conteudo | Quantidade |
|---|---|---|
| `Controllers/` (raiz) | Controllers de negocio + admin + diagnostico + storefront | ~75 |
| `Controllers/Ci/`, `Internal/`, `Public/` | Controllers especializados (auto-ticket, cron, leads) | 3 |
| `Mobile/Controllers/` | Controllers do app mobile (KDS, PWA, sync, etc.) | ~22 |
| `BackgroundServices/` | Jobs e workers | 17 |
| `Services/` | Servicos da camada Api (JWT, audit, helpdesk, etc.) | ~14 |
| `Observability/` | Health checks, metrics, exception handler, diagnosticos | 8 |
| `Configuration/` | Setup de DI, swagger, rate limit, options | 6 |
| `Authorization/` | InternalCronJob auth handler + options | 3 |
| `Middleware/` | Idempotency, security headers, subscription gate | 3 |
| `Http/` | Base controller, ApiResponse, attributes | 3 |
| `Data/` | Seed para tenants e bootstrap | ~10 |
| `Models/Fiscal/` | Request models de NF-e | ~9 |
| `Mobile/Services/`, `Schema/`, `Security/`, `DTOs/` | Apoio do modulo mobile | ~12 |

Tamanho: `Program.cs` = 943 linhas. `AdminClientesController.cs` = 641 linhas. `AdminSeedController.cs` = 483 linhas. `AdminNotificacoesController.cs` = 249 linhas com 20 dependencias no construtor.

---

## 3. Metodologia

### Fase 1 — Exploracao paralela (3 agentes)

| Agente | Escopo | Achados brutos |
|---|---|---|
| Agente Controllers | 75 controllers + 22 mobile controllers | 30 |
| Agente DI/Config/Auth/Middleware | Program.cs + Configuration/ + Authorization/ + Middleware/ + Http/ | 25 |
| Agente BackgroundServices/Services/Observability | 17 bg services + Services/ + Observability/ + Mobile/Services/ | 25 |

### Fase 2 — Validacao via medicao direta

Aplicada regra de memoria `feedback_medir_antes_afirmar.md`: toda afirmacao quantitativa exige 1 comando antes.

**Validacoes feitas:**

- `BadRequest(ex.Message)`: agente disse 65 → grep confirmou **137** em 37 arquivos
- `tenantId == Guid.Empty`: agente disse 17 → grep confirmou **23** em 7 arquivos
- `async Task<IActionResult>`: 430 ocorrencias em 76 arquivos vs **137** `CancellationToken` em 37 → confirma gap de ~293 actions
- `catch (Exception)`: 46 ocorrencias em 13 controllers (DiagnosticoLogsController tem 17 sozinho)
- Linhas reais (via `git show master:<path> | Measure-Object -Line`):
  - `Program.cs`: 943 (agente disse 1046)
  - `AdminClientesController.cs`: 641 (agente disse 729)
  - `AdminSeedController.cs`: 483 (agente disse 532)
- `IncrementFalhasOperacao` (MetricsService): grep retornou **0 callers** em todo o projeto → confirma dead code
- `MetricsService` injetado mas nao usado: confirmado nos 5 arquivos (Observability + 2 controllers + 2 config)

**Leituras diretas para validacao:**

- `JwtTokenService.cs` — confirmado Issuer/Audience opcionais sem fail-fast
- `SkiaImageProcessor.cs` — confirmado sem limite de tamanho na entrada
- `SecurityHeadersMiddleware.cs` — confirmado CSP com `'unsafe-inline'` (linhas 44-48)
- `InternalCronJobAuthHandler.cs` — confirmado `CryptographicEquals` custom (linhas 95-103)
- `GlobalExceptionHandler.cs` — **rejeitado**: impl correta, so vaza detalhes em dev (comportamento desejado)
- `Program.cs` (busca por "Sqlite") — **rejeitado**: existe `throw new InvalidOperationException` se resolver para SQLite em Production
- `AdminAdminsController.cs:26` — **rejeitado**: tem `public async Task<IActionResult> GetAdmins()` explicito
- `CobrancaAssinaturaJob.cs` — confirmado uso de `pg_try_advisory_lock` em PG; fallback para dev so com warning explicito → **rebaixado para P3**

### Fase 3 — Consolidacao e priorizacao

30 achados finais agrupados em 4 niveis (P0-P3) e 20 TASK-EZ-CR-* foram propostas.

---

## 4. Achados detalhados

### P0 — Seguranca / data loss

#### ACHADO-0 — Build quebrado em branch ativa

- **Severidade:** P0 (bloqueia merges sucessivos da branch)
- **Arquivo:** [EasyStock.Application/DependencyInjection/ServiceCollectionExtensions.Storefront.cs:3-4](../../../EasyStock.Application/DependencyInjection/ServiceCollectionExtensions.Storefront.cs)
- **Branch:** `feat/task-ez-agend-001-listar-janelas` (41 commits a frente do master)
- **Problema:** 2 erros CS0234 — namespace `EasyStock.Application.UseCases.Storefront.Checkout.*` e `Storefront.Checkout.Idempotency.*` nao existem. O arquivo registra `CheckoutIdempotencyService` e `IniciarCheckoutUseCase` que ainda nao foram escritos. Build quebra com:
  ```
  error CS0234: O nome de tipo ou namespace "Checkout" nao existe no namespace
  "EasyStock.Application.UseCases.Storefront"
  ```
- **Causa raiz:** commit incompleto deixou referencia fantasma. A pasta `Storefront/` tem apenas `Agendamento/` e `Avaliacao/`; `Checkout/` ainda nao existe.
- **Acao:** **R6 do CLAUDE.md — nao tocar.** Alertar dono da sessao TASK-EZ-AGEND-001. Se a feature Checkout foi descopada, o registro deve ser removido em um commit subsequente da propria branch.
- **Por que nao fix imediato:** essa branch deletou 8 controllers de Storefront (-2 mil linhas) e provavelmente esta refatorando para use cases. O fix correto vem do dono.

#### ACHADO-31 — Build do master quebrado: NSubstitute faltando em IntegrationTests

- **Severidade:** P0 (bloqueia CI + qualquer dev local)
- **Branch:** `master` (HEAD `72f59d3a`)
- **Arquivos:**
  - [EasyStock.Api.IntegrationTests/Storefront/Pedidos/PedidosClienteControllerTests.cs:13-14](../../../EasyStock.Api.IntegrationTests/Storefront/Pedidos/PedidosClienteControllerTests.cs)
  - [EasyStock.Api.IntegrationTests/EasyStock.Api.IntegrationTests.csproj](../../../EasyStock.Api.IntegrationTests/EasyStock.Api.IntegrationTests.csproj) (falta PackageReference)
- **Erros:**
  ```
  error CS0246: O nome do tipo ou do namespace "NSubstitute" nao pode ser encontrado
  ```
  6 ocorrencias.
- **Causa raiz:** commit `92c9b10b feat(TASK-EZ-PEDIDOS-001): green — use case + endpoint GET /pedidos cliente` adicionou o arquivo de teste usando `using NSubstitute;` e `using NSubstitute.ExceptionExtensions;` mas **nao adicionou** o `<PackageReference Include="NSubstitute" Version="..." />` ao csproj.
- **Outros projects test no projeto usam NSubstitute?** Verificar — pode ter sido copia-cola de outro test project que ja tinha o package.
- **Acao:** TASK-EZ-CR-021 — adicionar `<PackageReference Include="NSubstitute" />` ao `EasyStock.Api.IntegrationTests.csproj`. Provavelmente versao 5.x.
- **Por que e P0:** master quebrado bloqueia todos os PRs subsequentes; toda branch nova herda o estado quebrado; CI nunca fica verde.

---

#### ACHADO-1 — Vazamento de excecao via `BadRequest(ex.Message)` em 137 lugares

- **Severidade:** P0 (vazamento sistemico de internals)
- **Escopo:** 37 controllers
- **Top ofensores:**

| Controller | Ocorrencias |
|---|---|
| `AdminTicketsController` | 13 |
| `ConfiguracaoFiscalController` | 11 |
| `ContasAReceberController` | 9 |
| `ContasAPagarController` | 8 |
| `AdminClientesController` | 8 |
| `FaqAdminController` | 5 |
| `WebhookPixController` | 3 (webhook — sensivel) |

- **Padrao:**
  ```csharp
  try {
      // ... logica ...
  } catch (Exception ex) {
      return BadRequest(ex.Message);
  }
  ```
- **Por que e P0:** vaza tipo da excecao interna (ex.: `DbUpdateException`, `NpgsqlException`), paths de arquivo do stack, queries SQL parciais, mensagens de FK/constraint do Postgres com nomes de tabela e coluna, conteudo de configuracao via `ArgumentException`.
- **Ironia:** ja existe `GlobalExceptionHandler` ([EasyStock.Api/Observability/GlobalExceptionHandler.cs](../../../EasyStock.Api/Observability/GlobalExceptionHandler.cs)) que mapeia ~12 tipos de excecao em codigos pt-BR e nunca vaza stack em prod. Os try/catch dos controllers **contornam** o handler.
- **Acao:** remover try/catch dos controllers, deixar `GlobalExceptionHandler` mapear. Onde for inevitavel (webhooks com retry policy especifico, idempotencia), logar via `ILogger.LogError(ex, ...)` e retornar codigo generico (`return Problem(detail: "Falha na operacao", statusCode: 500)`).

---

### P1 — Bugs / contratos

#### ACHADO-2 — 293+ action methods sem `CancellationToken` (viola ADR-0013)

- **Severidade:** P1
- **Numeros:** 430 actions async em 76 controllers; apenas 137 referencias a `CancellationToken` em 37 arquivos. **Gap: ~293 actions** (algumas das 137 mencoes sao em variaveis locais, nao em parametros — gap real pode ser maior).
- **Impacto:**
  - Requisicoes longas (relatorios, exports, sync mobile, IA) nao cancelam quando cliente desconecta
  - Desperdicio de thread pool
  - Locks no DB segurando recursos
  - Background jobs com timeout perdem corrida com requests presos
- **ADR-0013** ja exige `CancellationToken ct` em `IUseCase.ExecuteAsync` — controllers nao estao propagando.
- **Acao:** sweep para adicionar `CancellationToken ct` em todo parametro de action async. Propagar ate `useCase.ExecuteAsync(input, ct)`.
- **Estrategia:** dividir em PRs por subdominio (Admin, Mobile, Webhooks, Diagnostico, Business, Storefront) para revisao manejavel.

#### ACHADO-3 — Race condition em `SyncMutationDispatcher.ApplyOrder`

- **Severidade:** P1
- **Arquivo:** [EasyStock.Api/Mobile/Services/SyncMutationDispatcher.cs](../../../EasyStock.Api/Mobile/Services/SyncMutationDispatcher.cs) — linhas aproximadas 270-300 e 420-442 (validar antes de PR)
- **Problema:** Em `ApplyOrder`, multiplas operacoes com side-effect (`CreateVendaForDeliveredOrderAsync`, `ApplyDeltaAsync` de estoque) sao executadas **antes** do `SaveChangesAsync()`. Excecao entre os awaits deixa estado inconsistente — Venda criada, Pedido nao marcado como "entregue".
- **Risco:** dados duplicados em sync concorrente do PWA, divergencia entre Pedidos.Status e Vendas existentes, estoque debitado sem registro de venda finalizada.
- **Acao:** envolver bloco com `await using var tx = await db.Database.BeginTransactionAsync(ct)` + `tx.Commit()` no fim, ou inverter ordem (persistir estado do Pedido antes de criar Venda).
- **Validacao adicional:** este achado foi sinalizado pelo agente Explore e nao foi lido diretamente pelo orquestrador (arquivo eh grande). Confirmar linhas exatas antes de fixar.

#### ACHADO-4 — Race + N+1 em `SyncAutoLinker`

- **Severidade:** P1
- **Arquivo:** [EasyStock.Api/Mobile/Services/SyncAutoLinker.cs](../../../EasyStock.Api/Mobile/Services/SyncAutoLinker.cs)
- **Problemas:**
  1. `EnsurePagamentoEntregueAsync` cria `PedidoPagamento` + `MovimentoCaixa` sem constraint unica `(pedidoId, metodo)` → duplicacao em sync concorrente do mesmo pedido.
  2. `TryAutoLinkBatchesAsync` itera `lote.Itens` chamando `_db.Set<Product>().FirstOrDefaultAsync()` por item — classico N+1. Lote com 10k itens = 10k queries.
- **Acao:**
  1. Migration para unique constraint `IX_PedidoPagamentos_PedidoId_Metodo` (UNIQUE)
  2. Carregar `Product` em lote via `WHERE Id IN (...)` + dicionario in-memory
- **Validacao adicional:** confirmar linhas no arquivo antes de PR.

#### ACHADO-5 — JwtTokenService sem fail-fast em config

- **Severidade:** P1
- **Arquivo:** [EasyStock.Api/Services/JwtTokenService.cs](../../../EasyStock.Api/Services/JwtTokenService.cs)
- **Codigo:**
  ```csharp
  Issuer = configuration["Jwt:Issuer"],     // aceita null
  Audience = configuration["Jwt:Audience"], // aceita null
  ```
- **Problemas:**
  1. `Jwt:Issuer` / `Jwt:Audience` ausentes — tokens gerados sem `iss`/`aud` claims; validacao em outro servico falha silenciosamente
  2. `Jwt:SecretKey` so faz `throw` se ausente, **nao valida length minimo** (`HmacSha256` precisa de chave >=32 bytes para seguranca minima)
  3. Sem politica de rotacao documentada (HS256 com chave compartilhada)
- **Acao:**
  - Validar Issuer/Audience/SecretKey no construtor com throw informativo
  - Documentar rotacao em ADR ou migrar para RS256 (par assimetrico)
  - Adicionar `appsettings.Production.json` template com placeholder
- **Validacao:** Confirmado por leitura direta do arquivo.

---

### P2 — Manutencao / clean code / SOLID

#### ACHADO-6 — `MetricsService` e 100% codigo morto

- **Severidade:** P2 (foi P1 do agente — rebaixado apos validacao)
- **Arquivo:** [EasyStock.Api/Observability/MetricsService.cs](../../../EasyStock.Api/Observability/MetricsService.cs)
- **Validacao:** grep confirmou **0 chamadores** para `IncrementEntradasEstoque`, `IncrementSaidasEstoque`, `IncrementReposicoesEstoque`, `IncrementVendas`, `IncrementFalhasOperacao` em todo `EasyStock.Api/`.
- **Estado:** servico registrado em DI, injetado em `ReportsController` e `AdminReportsController` como dependencia inerte. Nenhum `Counter<long>` e incrementado em runtime.
- **Decisao necessaria:**
  - **Opcao A:** deletar `MetricsService.cs` + remover registro DI + remover injecao nos 2 controllers
  - **Opcao B:** instrumentar use cases reais (RegistrarEntrada, RegistrarSaida, RegistrarVenda) para ativar metricas
- **Recomendacao:** Opcao A se nao houver demanda concreta por Prometheus/Grafana neste projeto. Custo de manter codigo morto > custo de reescrever quando precisar.

#### ACHADO-7 — 8 controllers de Storefront sendo deletados na branch ativa, sem ADR

- **Severidade:** P2 (governance/historia)
- **Branch:** `feat/task-ez-agend-001-listar-janelas`
- **Deletados (delta versus master):**

| Arquivo | Linhas removidas |
|---|---|
| `Controllers/Storefront/CheckoutController.cs` | 229 |
| `Controllers/Storefront/AprovacaoPedidoController.cs` | 246 |
| `Controllers/Storefront/MenuController.cs` | 131 |
| `Controllers/Storefront/FreteController.cs` | 106 |
| `Controllers/Storefront/PedidosClienteController.cs` | 134 |
| `Controllers/Storefront/AuthController.cs` (storefront) | 210 |
| `Controllers/AdminStorefrontController.cs` | 186 |
| `Controllers/AdminStorefrontCardapioController.cs` | 194 |
| `Middleware/ClienteSessionMiddleware.cs` | 48 |
| `Authorization/ClienteSessionAuthenticationHandler.cs` | 81 |
| `BackgroundServices/ExpirarClienteSessionsBackgroundService.cs` | 68 |

- **Total:** -2.000 linhas
- **Problema:** ao mergear esta branch, master perde controllers historicos. Se nao houver ADR explicando "por que foi removido / para onde foi", a historia se perde.
- **Acao:** dono da branch deve criar ou referenciar ADR (provavelmente ADR-EZ-AGEND ou ADR-Storefront-Refactor) registrando a decisao.

#### ACHADO-8 — `AdminNotificacoesController` com 20 dependencias

- **Severidade:** P2 (SRP)
- **Arquivo:** [EasyStock.Api/Controllers/AdminNotificacoesController.cs:14-34](../../../EasyStock.Api/Controllers/AdminNotificacoesController.cs)
- **Construtor (validado por leitura):** 6 repositorios + 1 `ICurrentUserAccessor` + 13 use cases = **20 deps**. 249 linhas, ~15 actions.
- **Problema:** controller virou um "templates + rotinas + canais + bloqueios + consentimentos + variaveis + broadcast" tudo junto. SRP violado.
- **Acao:** splittar em:
  - `AdminTemplatesController` (templates + variaveis + preview)
  - `AdminRotinasController` (rotinas + kill switch)
  - `AdminBroadcastController` (broadcast + logs + bloqueios + consentimentos)

  Manter rota base `/api/admin/notificacoes/...` com subpatas para nao quebrar clientes.

#### ACHADO-9 — `AdminClientesController` com 641 linhas + actions de 100+ linhas

- **Severidade:** P2
- **Arquivo:** [EasyStock.Api/Controllers/AdminClientesController.cs](../../../EasyStock.Api/Controllers/AdminClientesController.cs)
- **Pontos especificos (validados por leitura):**
  - `GetAtividade` (~186-300, 115 linhas) — mescla `AuditLog` (tenant) + `AdminAuditLog` (operador) em memoria com paginacao manual `Skip((page-1)*pageSize)`. **O proprio autor anotou no codigo:** _"em P1 isso e OK; em P2 pode virar query SQL nativa com UNION ALL pra eficiencia"_
  - `ExportarDados` (~423-496, 74 linhas) — monta ZIP no controller
  - `GetUsuarioPii` (~300-370, 70 linhas) — validacao + mascaramento + audit log
  - 12 ocorrencias de `tenantId == Guid.Empty` (duplicacao)
  - Helpers privados `MascararDetalhes`, `MascararIp`, `MascararEmail` — **ja existe `Utilities/PiiMaskingHelper.cs` no proprio projeto!**
- **Acao:**
  - Extrair `GetAtividadeUseCase` na Application com UNION ALL via repository
  - Substituir helpers privados por uso de `PiiMaskingHelper` existente
  - Extrair `ExportarDadosAdminUseCase`
  - Considerar splittar Lojas/Notas/PII em controllers proprios

#### ACHADO-10 — `AdminSeedController` com 483 linhas

- **Severidade:** P2
- **Arquivo:** [EasyStock.Api/Controllers/AdminSeedController.cs](../../../EasyStock.Api/Controllers/AdminSeedController.cs)
- **Problema:** controller virou servico — metodos de seed inline com logica de dados embarcada. Mais de `Data/Tenants/*Seed.cs` ja existem como classes de seed.
- **Acao:** extrair `IDbSeedService` em `EasyStock.Application` ou consolidar com as classes em `Data/Tenants/`.

#### ACHADO-11 — `Program.cs` com 943 linhas

- **Severidade:** P2
- **Arquivo:** [EasyStock.Api/Program.cs](../../../EasyStock.Api/Program.cs)
- **Problema:** mistura startup + DI + migrations + seeding + JWT validation + middleware pipeline + banner ASCII de log + validacao de chaves perigosas.
- **Acao:** extrair extension methods em `ProgramExtensions/`:
  - `StartupSecurity.cs` — JWT validation, CORS, rate limit
  - `StartupMigrations.cs` — `dotnet ef database update` flow
  - `StartupBanner.cs` — log de boot
  - `StartupHardening.cs` — checagem de chaves perigosas em prod

  Meta: `Program.cs` com ~300 linhas.

#### ACHADO-12 — `AddEasyStockRateLimit` com ~110 linhas inline

- **Severidade:** P2
- **Arquivo:** [EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs:169-280](../../../EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs)
- **Problema:** 8 politicas de rate limit (ai, tickets-post, public-read, public-post, signup, disponibilidade, ...) inline em um metodo gigante.
- **Acao:** extrair `RateLimitingPolicies` static class com 1 metodo por politica. Bonus: documentar limites em ADR.

#### ACHADO-13 — 23 validacoes manuais `if (X == Guid.Empty)` em 7 arquivos

- **Severidade:** P2 (DRY/manutencao)
- **Distribuicao:**

| Arquivo | Ocorrencias |
|---|---|
| `AdminClientesController` | 12 |
| `EntityAuditController` | 3 |
| `Http/EasyStockControllerBase` | 2 |
| `Mobile/Controllers/MobileManagementControllerBase` | 2 |
| `Middleware/IdempotencyMiddleware` | 1 |
| `Mobile/Controllers/DevicePairingController` | 1 |
| `Mobile/Controllers/OperationController` | 2 |

- **Padrao:**
  ```csharp
  if (tenantId == Guid.Empty) return DataBadRequest("Cliente invalido.");
  ```
- **Acao:**
  - Criar `EasyStockControllerBase.RequireGuid(Guid value, string nome)` helper, ou
  - Criar `[ValidGuid]` attribute para model binding, ou
  - Usar `IRouteConstraint` (`{tenantId:guid}` ja garante formato — a redundancia e mais por defesa)

#### ACHADO-14 — CSP com `'unsafe-inline'`

- **Severidade:** P2 (divida documentada)
- **Arquivo:** [EasyStock.Api/Middleware/SecurityHeadersMiddleware.cs:44-48](../../../EasyStock.Api/Middleware/SecurityHeadersMiddleware.cs)
- **Codigo (validado por leitura):**
  ```csharp
  // CSP: balanceado entre paginas HTML servidas (Diagnostico) e API JSON.
  // 'unsafe-inline' em script/style necessario enquanto Diagnostico usa inline; tightening futuro com nonces.
  headers["Content-Security-Policy"] =
      "default-src 'self'; " +
      "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
      "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
      ...
  ```
- **Problema:** o proprio codigo reconhece. Refatorar paginas Diagnostico para nao usar inline scripts/styles e aplicar nonce por requisicao.
- **Acao:** P2 medio prazo.

#### ACHADO-15 — `SkiaImageProcessor.Optimize` sem limite de tamanho

- **Severidade:** P2 (DoS por imagem grande)
- **Arquivo:** [EasyStock.Api/Services/SkiaImageProcessor.cs:13-16](../../../EasyStock.Api/Services/SkiaImageProcessor.cs)
- **Codigo:**
  ```csharp
  public (byte[] Data, string ContentType, string Extension) Optimize(
      byte[] source,
      string originalContentType,
      int maxSide = 1920,
      int quality = 85)
  ```
- **Problema:** aceita `byte[]` arbitrario. `SKBitmap.Decode` pode alocar memoria sem limite em imagens maliciosas (bomb decompressao).
- **Mitigacao parcial:** validacao real precisa estar tambem no upload (provavel `UploadsController`).
- **Acao:**
  - Guard `if (source.Length > MaxBytes) throw new ArgumentException(...);` no comeco de `Optimize`
  - `[RequestSizeLimit(MaxBytes)]` no `UploadsController` action
  - Documentar limite em ADR

#### ACHADO-16 — `AdminAuditService.LogAsync` nao trunca `motivo`

- **Severidade:** P2 (LGPD/PII + indice)
- **Arquivo:** [EasyStock.Api/Services/AdminAuditService.cs:28](../../../EasyStock.Api/Services/AdminAuditService.cs)
- **Problema:** `motivo` (texto digitado pelo operador admin para compliance LGPD) vai direto para `AdminAuditLog.Criar(...)`. Se Domain nao validar tamanho, pode bloquear indice no Postgres com text de 100KB.
- **Acao:** validar no construtor de `AdminAuditLog.Criar` (no Domain) ou truncar no service: `motivo = motivo?[..Math.Min(motivo.Length, 500)]`.

#### ACHADO-17 — 46 `catch (Exception)` em 13 controllers; DiagnosticoLogsController concentra 17

- **Severidade:** P2
- **Distribuicao:**

| Arquivo | Ocorrencias |
|---|---|
| `DiagnosticoLogsController` | 17 |
| `DiagnosticoInfraController` | 6 |
| `DiagnosticoController` | 4 |
| `AdminClientesController` | 4 |
| `AdminSeedController` | 4 |
| `WebhookPixController` | 3 |
| `AdminUsuariosTenantController` | 2 |
| outros | 6 |

- **Problema:** muitas variantes do padrao "garante 200 OK no diagnostico". Podem mascarar bugs reais.
- **Acao:** auditar caso-a-caso. Engolir so se:
  1. `LogError(ex, ...)` esta presente, e
  2. Comentario explica a razao (ex: "diagnostico nao pode quebrar a pagina")

#### ACHADO-18 — `InternalCronJobAuthHandler` usa `CryptographicEquals` custom

- **Severidade:** P2 (home-rolled crypto)
- **Arquivo:** [EasyStock.Api/Authorization/InternalCronJobAuthHandler.cs:95-103](../../../EasyStock.Api/Authorization/InternalCronJobAuthHandler.cs)
- **Codigo atual:**
  ```csharp
  private static bool CryptographicEquals(string a, string b)
  {
      if (a.Length != b.Length) return false;
      var diff = 0;
      for (var i = 0; i < a.Length; i++)
          diff |= a[i] ^ b[i];
      return diff == 0;
  }
  ```
- **Problema:** impl parece correta (XOR time-constant), mas .NET ja tem `CryptographicOperations.FixedTimeEquals(ReadOnlySpan<byte>, ReadOnlySpan<byte>)` testado e auditado.
- **Acao:**
  ```csharp
  using System.Security.Cryptography;
  ...
  var providedBytes = Encoding.UTF8.GetBytes(providedToken);
  var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
  if (providedBytes.Length != expectedBytes.Length ||
      !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
      return Fail("Token invalido.");
  ```

#### ACHADO-19 — Request/response records dentro de controllers

- **Severidade:** P2 (vazamento de boundary)
- **Exemplos:**
  - `AdminClientesController.cs:725+` define `CriarLojaAdminRequest`, `AtualizarLojaAdminRequest`
  - `AdminAdminsController.cs:155` define `CreateAdminRequest`
- **Problema:** records de contrato deveriam estar em `EasyStock.Contracts` ou `EasyStock.Api/Contracts/<dominio>/` para reuso e versionamento.
- **Acao:** mover progressivamente conforme controllers sao tocados.

---

### P3 — Cosmetico / doc / style

#### ACHADO-20 — Comentarios em ingles (viola CLAUDE.md + ADR-0011)

- **Severidade:** P3
- **Estimativa:** 30+ comentarios em ingles em controllers admin
- **Exemplos:**
  - `AdminAdminsController.cs:61` — `// Find or create the SuperAdmin perfil`
  - `AdminApkReleaseController.cs:11-20` — xmldoc inteiro em ingles
  - `AdminAuditLogsController.cs:79` — `// Mask PII before returning`
  - `AdminBuscaGlobalController.cs:35-123` — varios comentarios em ingles
  - `AdminClientesController.cs:180-234` — xmldoc + comentarios em ingles
- **Acao:** sweep em batch (1 PR por sub-pasta para revisao manejavel).

#### ACHADO-21 — `Options` sem `.ValidateOnStart()`

- **Severidade:** P3
- **Arquivo:** [EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs:145-161](../../../EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs)
- **Problema:** `Configure<InternalCronJobOptions>()`, `BackgroundJobOptions`, etc. sao registrados sem `.ValidateDataAnnotations().ValidateOnStart()`. Falhas de bind aparecem so quando o request chega — em prod isso vira 500 ao inves de fail-fast no startup.
- **Acao:**
  ```csharp
  services.AddOptions<InternalCronJobOptions>()
      .Bind(configuration.GetSection("Notifications:CronJob"))
      .ValidateDataAnnotations()
      .ValidateOnStart();
  ```

#### ACHADO-22 — `CacheWarmupService` (validar comportamento de startup)

- **Severidade:** P3 (a validar)
- **Acao:** verificar se ha `await Task.WhenAll(...)` bloqueante no `ExecuteAsync` que atrase cold start em prod.

#### ACHADO-23 — Combine com ACHADO-19 (records em controllers).

#### ACHADO-24 — Falta `AsNoTracking()` em queries somente leitura

- **Severidade:** P3 (perf)
- **Locais:** varios reports controllers, `AdminAuditLogsController.cs:39-40`.
- **Acao:** auditar queries de read-only e aplicar `.AsNoTracking()`.

#### ACHADO-25 — `GlobalUsings.cs` expoe `EasyStock.Api.Services` globalmente

- **Severidade:** P3 (acoplamento)
- **Arquivo:** [EasyStock.Api/GlobalUsings.cs](../../../EasyStock.Api/GlobalUsings.cs)
- **Problema:** `global using EasyStock.Api.Services;` faz todos os Services serem visiveis em qualquer arquivo do assembly sem import explicito. Reduz discoverability.
- **Acao:** remover (forcar `using EasyStock.Api.Services;` explicito onde necessario).

#### ACHADO-26 — `NoWarn NU1903` no csproj

- **Severidade:** P3 (security/freshness)
- **Arquivo:** [EasyStock.Api/EasyStock.Api.csproj](../../../EasyStock.Api/EasyStock.Api.csproj)
- **Problema:** `<NoWarn>NU1903</NoWarn>` suprime aviso de pacote vulneravel (SharpCompress). Comentario do csproj diz "reavalie quando patch sair" — provavelmente esquecido.
- **Acao:** rodar `dotnet list package --vulnerable` e remover o NoWarn se ha patch.

#### ACHADO-27 — Mudancas em `ConfigurationKeys` na branch atual

- **Severidade:** P3 (a validar pos-merge da branch ativa)
- **Acao:** quando branch `feat/task-ez-agend-001-listar-janelas` mergear, revisar consistencia de chaves de configuracao.

#### ACHADO-28 — `MobileQuickReportsController` recebeu +74 linhas na branch atual

- **Severidade:** P3 (escopo de outra revisao)
- **Acao:** revisar quando branch mergear; fora do escopo desta auditoria (R6).

#### ACHADO-29 — `SubscriptionGateMiddleware.TrialExpiradoSemPlanoAtivo()` sem testes de boundary

- **Severidade:** P3 (cobertura)
- **Arquivo:** [EasyStock.Api/Middleware/SubscriptionGateMiddleware.cs](../../../EasyStock.Api/Middleware/SubscriptionGateMiddleware.cs)
- **Acao:** adicionar testes para `DataFim == now`, `DataFim == now + 1s`, `DataFim == null`.

#### ACHADO-30 — `ValidateEmpresaIdAttribute` cache de reflection sem limite

- **Severidade:** P3 (memory leak leve)
- **Arquivo:** [EasyStock.Api/Http/ValidateEmpresaIdAttribute.cs:63-68](../../../EasyStock.Api/Http/ValidateEmpresaIdAttribute.cs)
- **Problema:** `ConcurrentDictionary` cresce indefinidamente conforme novos tipos com `EmpresaId` aparecem. Em apps long-running com muitos tipos, vira memory leak.
- **Acao:** size cap LRU (ex.: max 1000 tipos, evict LRU).

---

## 5. Roadmap — TASK-EZ-CR-* propostas

Cada TASK roda em worktree proprio (`.claude/worktrees/wt-cr-NNN`) seguindo Sistema ETK (ADR-0020), com TDD e PR + admin-squash-merge.

| Task | Prioridade | Esforco | Achado | Resumo |
|---|---|---|---|---|
| TASK-EZ-CR-001 | P0 | M | #1 | Remover 137 `BadRequest(ex.Message)` — 5 PRs (Admin, Mobile, Webhooks, Diagnostico, Business) |
| TASK-EZ-CR-002 | P1 | G | #2 | Adicionar `CancellationToken ct` em ~293 actions — PRs por subdominio |
| TASK-EZ-CR-003 | P1 | M | #3, #4 | Transacao + unique constraints em SyncMutationDispatcher/SyncAutoLinker; tests de race |
| TASK-EZ-CR-004 | P1 | P | #5 | Validar Issuer/Audience/Secret no construtor de JwtTokenService; doc rotacao |
| TASK-EZ-CR-005 | P2 | P | #6 | Decidir: deletar MetricsService OU instrumentar use cases reais |
| TASK-EZ-CR-006 | P2 | M | #8 | Splittar AdminNotificacoesController em 3 controllers |
| TASK-EZ-CR-007 | P2 | M | #9 | Refatorar AdminClientesController.GetAtividade para UseCase + UNION ALL; usar PiiMaskingHelper |
| TASK-EZ-CR-008 | P2 | M | #10 | Mover logica de AdminSeedController para IDbSeedService |
| TASK-EZ-CR-009 | P2 | M | #11 | Extrair Program.cs em extension methods (~300 linhas final) |
| TASK-EZ-CR-010 | P2 | P | #12 | Extrair RateLimitingPolicies static class |
| TASK-EZ-CR-011 | P2 | P | #13 | RequireGuid helper em EasyStockControllerBase + remover 23 validacoes manuais |
| TASK-EZ-CR-012 | P2 | P | #15, #16 | Tamanho maximo SkiaImageProcessor + truncamento AdminAuditLog |
| TASK-EZ-CR-013 | P2 | P | #17 | Auditar 46 `catch (Exception)` em controllers |
| TASK-EZ-CR-014 | P2 | P | #18 | Substituir CryptographicEquals por CryptographicOperations.FixedTimeEquals |
| TASK-EZ-CR-015 | P2 | M | #14 | Refatorar Diagnostico pages para remover `'unsafe-inline'` da CSP |
| TASK-EZ-CR-016 | P3 | M | #20 | Sweep comentarios ingles → pt-BR |
| TASK-EZ-CR-017 | P3 | P | #21 | Adicionar `.ValidateOnStart()` em Configure<T>() |
| TASK-EZ-CR-018 | P3 | P | #26 | Verificar `dotnet list package --vulnerable` e remover `NoWarn NU1903` se patch existir |
| TASK-EZ-CR-019 | P3 | P | #30 | Cap LRU no cache de reflection do ValidateEmpresaIdAttribute |
| TASK-EZ-CR-020 | P3 | P | #29 | Boundary tests para SubscriptionGateMiddleware.TrialExpiradoSemPlanoAtivo |
| TASK-EZ-CR-021 | P0 | P | #31 | Adicionar PackageReference NSubstitute ao EasyStock.Api.IntegrationTests.csproj |

**Tamanho:** P = pequeno (<1 dia), M = medio (1-3 dias), G = grande (>3 dias).
**Total:** 21 tasks. P0/P1: 5 tasks. P2: 11 tasks. P3: 5 tasks.

---

## 6. Apendice — falsos positivos rejeitados

Tres achados dos agentes Explore foram rejeitados apos validacao direta:

### Rejeitado 1: "Fallback automatico SQLite em Producao" (Program.cs)

- **Agente:** DI/Config (severidade CRITICA original)
- **Realidade:** o codigo TEM fail-fast explicito:
  ```csharp
  // Fail-fast: nunca subir em producao usando SQLite (seria banco local efemero no container)
  if (resolvedProvider == "sqlite" && builder.Environment.IsProduction())
      throw new InvalidOperationException(
          "PostgreSQL indisponivel e SQLite nao e permitido em Production. ...");
  ```
- **Conclusao:** impl correta. Agente confundiu fluxo de auto-detect com fluxo de fallback final.

### Rejeitado 2: "GlobalExceptionHandler vaza stack ao cliente em dev"

- **Agente:** Services/Observability (severidade MEDIO-ALTO original)
- **Realidade:** o codigo CHECA `IsDevelopment()` e so vaza nesse caso:
  ```csharp
  errorDetail = isDevelopment
      ? $"{exception.GetType().Name}: {exception.Message}..."
      : "Ocorreu um erro inesperado. Use o CorrelationId para rastreamento.";
  ```
- **Conclusao:** comportamento correto e desejado. Dev precisa ver excecao; prod nunca ve.

### Rejeitado 3: "AdminAdminsController.cs:26 sem tipo de retorno"

- **Agente:** Controllers (severidade P1 original)
- **Realidade:** linha 26 e `public async Task<IActionResult> GetAdmins()` com tipo explicito.
- **Conclusao:** alarme falso.

---

## 7. Apendice — rebaixamentos apos validacao

### Rebaixado 1: `MetricsService` cardinalidade explosiva (P1 → P3 + reclassificado como dead code)

- **Achado original:** "Cardinalidade explosiva via `operacao` sem whitelist em `IncrementFalhasOperacao`"
- **Validacao:** grep retornou **0 callers** para qualquer metodo Increment* do `MetricsService`. O servico esta registrado e injetado mas nunca usado.
- **Conclusao:** nao ha cardinalidade explosiva porque nao ha cardinalidade — e codigo morto. Severidade real: P2 (dead code para remover ou instrumentar use cases).

### Rebaixado 2: `CobrancaAssinaturaJob` lock distribuido (P1 → P3)

- **Achado original:** "Em prod com multiplos pods, CobrancaAssinatura roda N vezes em paralelo"
- **Validacao:** o codigo usa `pg_try_advisory_lock` (PostgreSQL nativo) com key inteiro, retornando cedo se outra replica adquiriu. Fallback para "sem lock" so acontece em DEV com SQLite — e tem `LogWarning` explicito.
- **Conclusao:** impl correta para multi-pod prod. Achado real: documentar melhor o caminho dev-only ou abortar startup se nao-PG em prod.

---

## 8. Apendice — metodologia detalhada

### Comandos de validacao executados

```powershell
# Contagem de ofensores
Grep BadRequest\(ex\.Message\)|BadRequest\(new \{ erro = ex\.Message → 137 (37 arquivos)
Grep tenantId == Guid\.Empty|empresaId == Guid\.Empty → 23 (7 arquivos)
Grep async Task<IActionResult> → 430 (76 arquivos)
Grep CancellationToken (em Controllers) → 137 (37 arquivos)
Grep catch \(Exception → 46 (13 arquivos)
Grep Increment(Entradas|Saidas|Reposicoes|Vendas|Falhas)Operacao → 5 (so em MetricsService.cs — 0 callers)

# Tamanhos
git show master:EasyStock.Api/Program.cs | Measure-Object -Line → 943
git show master:EasyStock.Api/Controllers/AdminClientesController.cs → 641
git show master:EasyStock.Api/Controllers/AdminSeedController.cs → 483
git show master:EasyStock.Api/Controllers/AdminNotificacoesController.cs → 249

# Leituras integrais
git show master:EasyStock.Api/Services/JwtTokenService.cs
git show master:EasyStock.Api/Services/SkiaImageProcessor.cs
git show master:EasyStock.Api/Middleware/SecurityHeadersMiddleware.cs
git show master:EasyStock.Api/Authorization/InternalCronJobAuthHandler.cs
git show master:EasyStock.Api/Observability/GlobalExceptionHandler.cs
git show master:EasyStock.Api/Observability/MetricsService.cs
git show master:EasyStock.Api/Services/AdminAuditService.cs
git show master:EasyStock.Api/BackgroundServices/CobrancaAssinaturaJob.cs
git show master:EasyStock.Api/Controllers/AdminNotificacoesController.cs (primeiras 50 linhas)
git show master:EasyStock.Api/Controllers/AdminAdminsController.cs (primeiras 35 linhas)
```

### Limites desta auditoria

- **Nao validei a fundo:** `SyncMutationDispatcher.cs` e `SyncAutoLinker.cs` (arquivos grandes; achados P1 vieram do agente Explore e devem ser confirmados antes de PR)
- **Nao desci em:** `EasyStock.Application`, `EasyStock.Domain`, `EasyStock.Contracts`, `EasyStock.Infra.*` (fora do escopo combinado com o Felipe)
- **Nao testei runtime:** analise eh 100% estatica. Bugs visiveis so em runtime (race conditions, leaks reais) precisam de instrumentacao.
- **Falsos positivos podem existir:** os 3 ja identificados estao documentados. Outros podem aparecer ao implementar as tasks — cada PR vai validar.

---

## 9. Referencias

- ADR-0011 — Nomenclatura pt-BR Rotulagem
- ADR-0013 — CancellationToken em IUseCase
- ADR-0020 — Sistema ETK (worktree por task)
- CLAUDE.md v2.1 — protocolo operacional
- Memorias:
  - `feedback_medir_antes_afirmar.md`
  - `feedback_self_review_plano.md`
  - `gh-pr-merge-master-worktree-gotcha.md`

---

**Fim do relatorio.** As TASK-EZ-CR-001 ate TASK-EZ-CR-020 sao registradas em [docs/tasks/inbox/](../tasks/inbox/) (proximo passo) e ficam aguardando priorizacao do Felipe.
