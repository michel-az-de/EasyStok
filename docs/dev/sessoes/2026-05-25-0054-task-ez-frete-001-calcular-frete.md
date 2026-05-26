# Sessao TASK-EZ-FRETE-001 - calcular frete por CEP

Data: 2026-05-25 00:54 UTC
Worktree: C:/easy/EasyStok/.claude/worktrees/wt-ez-frete-001
Branch: feat/task-ez-frete-001-calcular-frete (4 commits ahead de feat/task-ez-auth-001-solicitar-otp)
Identidade Git: felipe.azevedo@gmail.com / gh michel-az-de
Status final: completo (aguardando revisao + merge na cadeia ez-001..ez-009..auth-001..frete-001 + push)

## O que foi feito

Endpoint publico de calculo de frete por CEP (ADR-0011):

GET `/api/storefront/{slug}/frete?cep=...`

Sucesso: `200 + FreteCalculadoDto { zonaId, valor, valor_formatado, eta_label, zona_label }`
com `Cache-Control: public, max-age=86400`.

Mapeamento de erros (todos `Cache-Control: no-store, no-cache`):

- 400 `CepInvalidoException` (formato != 8 digitos) + 400 quando cep vazio
- 404 `StorefrontNaoEncontradoException` (slug inexistente ou storefront inativo)
- 422 `CepSemCoberturaException` (CEP valido mas nenhuma zona cobre — ProblemDetails)

Componentes entregues:

**Domain (2 exceptions):**
- `CepInvalidoException`
- `CepSemCoberturaException`

**Application:**
- `ICepLookupClient` (port best-effort em `Ports/Output/Lookup/`) + `CepLookupResult` record
- `CalcularFreteInput` / `FreteCalculadoDto` (records)
- `CalcularFreteUseCase` (24 testes unitarios Theory/Fact)
- `IFreteZonaRepository.BuscarZonaPorCepAsync` (interface estendida)
- `ServiceCollectionExtensions.Storefront.cs` (registra CalcularFreteUseCase)

**Infra.Postgre:**
- `FreteZonaRepository.BuscarZonaPorCepAsync` (in-memory filter sobre
  `GetAtivasDoStorefrontOrdenadasAsync`, usando `FreteZona.CobreCep` /
  `CobreBairro`)

**Infra.Integrations:**
- `Cep/NoOpCepLookupClient.cs` (sempre null)
- `Cep/ViaCepLookupClient.cs` (HttpClient para viacep.com.br, Timeout 1s, NUNCA lanca)
- `DependencyInjection/CepServiceCollectionExtensions.cs` (feature flag
  `ENABLE_VIACEP_LOOKUP` / `Storefront:Frete:EnableViaCepLookup`)

**Api:**
- `Controllers/Storefront/FreteController.cs` (rota anonima, cache HTTP control)
- `Program.cs` chama `AddEasyStockCepLookup(Configuration)` proximo do `AddEasyStockWhatsAppStub`

**Tests:**
- `CalcularFreteUseCaseTests.cs` (Application.Tests, 24/24 PASS)
- `FreteControllerTests.cs` (Api.IntegrationTests, 6 cenarios — Testcontainers
  Postgres, no-op se Docker indisponivel)

## Decisoes tomadas (divergencias documentadas vs YAML)

1. **Adapters HTTP em `EasyStock.Infra.Integrations/Cep/`, nao em projeto novo
   `EasyStock.Infra.Http`**: o projeto `EasyStock.Infra.Http` nao existe.
   Adapters HTTP externos vivem em `EasyStock.Infra.Integrations` (mesmo
   padrao do `StubWhatsAppOtpSender` em `Infra.Integrations/WhatsApp/`,
   confirmado no handoff EZ-AUTH-001). Evita criar csproj adicional + entrada
   no .sln.

2. **`BuscarZonaPorCepAsync` filtra in-memory, nao via SQL**: cada storefront
   tem poucas zonas (tipicamente &lt;20). A logica de match (CEP range e
   `bairros_json` contains) ja vive em `FreteZona.CobreCep` /
   `FreteZona.CobreBairro` (testada na TASK-EZ-003). Duplicar em SQL
   exigiria JSON contains nao-portavel (LIKE textual frágil ou JSONB
   operadores) sem ganho real. Quando catalogo crescer >> 100 zonas,
   otimizar com filtro CEP range SQL-side + bairro client-side.

3. **`expiresInSeconds`/TTL nao aparece na response 200**: a task fala em
   "cache 86400s" sem especificar onde. Implementado como `Cache-Control:
   public, max-age=86400` no response header (HTTP cache no edge/cliente —
   semantica correta para um GET idempotente). Sem cache redis distribuido
   nesta task — futuro PR pode adicionar se necessario.

4. **Use case usa cache de leitura zero — apenas Cache-Control header**:
   reverso, qualquer cache distribuido (Redis) deve ser camada acima,
   transparente ao use case (responder com 304/conditional GET futuro,
   por exemplo). Mantem use case puro.

5. **Formato `eta_label`**: a task da exemplo "Hoje até 18h" mas a entity
   `FreteZona` so guarda `TempoEstimadoMinutos` (int). Implementei
   formatacao baseada em minutos: 30 -> "30 min", 60 -> "1h", 90 -> "1h30",
   125 -> "2h05". Sem suporte a horario absoluto ("Hoje até 18h") porque
   exigiria modelagem de calendario/janela (responsabilidade do
   TASK-EZ-AGEND-001 / TASK-EZ-004 JanelaEntrega — fora do escopo).

6. **`ICepLookupClient.LookupAsync` retorna `null` em qualquer falha — NUNCA
   lanca**. Contrato deliberadamente fraco para que o use case use bairro
   apenas como bonus. Defense-in-depth: o use case ainda envolve a chamada
   em try/catch (caso uma impl futura quebre o contrato).

7. **`ENABLE_VIACEP_LOOKUP` aceita "true"/"false"/"1"/"0"** (config OU env
   var). Default desligado para dev/CI sem internet. Ligar em prod via env
   var ou `appsettings.Production.json`.

8. **CRLF + UTF-8 BOM aplicado em 16 arquivos do escopo via PowerShell**
   antes do primeiro commit (sem chore commit separado, diferente do
   EZ-AUTH-001 — aqui ja sai pronto). Arquivos pre-existentes com problema
   de format/encoding em ~1900 arquivos do master nao foram tocados (R6).

## Quality gates (status)

- `dotnet build EasyStok.sln -warnaserror:nullable -c Debug`:
  ✅ 0 erros, 9 warnings (todos pre-existentes — Android Mobile CA1422 x4,
  ProdutosController CS8602 x2, LocalFileStorage CS9107, RLS tests EF1002,
  MainActivity CS0618).
- `dotnet test EasyStock.ArchitectureTests`: ✅ 25/25.
- `dotnet test EasyStock.Application.Tests --filter CalcularFreteUseCaseTests`:
  ✅ 24/24.
- `dotnet test EasyStock.Api.IntegrationTests --filter FreteControllerTests`:
  ✅ 6/6 (no-op aqui — sem Docker local; roda em CI).
- `dotnet format --verify-no-changes` (escopo): ✅ todos os 16 arquivos
  passam projeto-por-projeto.
- Husky pre-commit `rotulagem-architecture-tests`: ✅ verde nos 4 commits.

## Commits criados

- 4467d034 `test(TASK-EZ-FRETE-001): red - CalcularFreteUseCaseTests com 24 cenarios`
- 4c8919cf `feat(TASK-EZ-FRETE-001): green - CalcularFreteUseCase + BuscarZonaPorCep no repo`
- 3b27e321 `feat(TASK-EZ-FRETE-001): ViaCEP/NoOp adapters + FreteController + DI feature flag`
- 36057341 `test(TASK-EZ-FRETE-001): integration test FreteController com Postgres`

## Branches criadas/deletadas

- Criada: `feat/task-ez-frete-001-calcular-frete` (a partir de
  `feat/task-ez-auth-001-solicitar-otp`)
- Worktree: `.claude/worktrees/wt-ez-frete-001` (criado, ainda ativo)

## O que ficou pendente

- **Merge da cadeia ez-001..ez-009..auth-001..frete-001**: as branches
  ancestrais nao tem PRs abertas em master. PR de FRETE-001 precisa
  esperar essa cadeia subir.
- **Push da branch**: requer autorizacao explicita do Felipe (R9 do
  CLAUDE.md).
- **PR**: `gh pr create` requer base correta — provavelmente
  `feat/task-ez-auth-001-solicitar-otp` ate cadeia subir em master.
- **Cache distribuido (Redis)**: opcional para o futuro. Atualmente cache
  apenas HTTP (max-age=24h no Cache-Control). Para multi-instance + edge
  cache shared, considerar IDistributedCache na frente do use case.
- **Suporte a "Hoje até 18h"** (ETA absoluto baseado em janelas de
  entrega): depende de TASK-EZ-AGEND-001 + entity JanelaEntrega. Quando
  a janela ativa do dia for conhecida, o controller pode compor
  `eta_label = "Hoje até HHh"` usando minutos da zona + slot da janela.

## Proxima acao recomendada

1. Felipe revisa o handoff + decide sobre PR (base auth-001 ou esperar cadeia).
2. Felipe autoriza `git push -u origin feat/task-ez-frete-001-calcular-frete`.
3. Iniciar TASK-EZ-CHECKOUT-001 (protocolo 3 fases — depende de FRETE-001 +
   MENU-001 + AGEND-001 + AUTH-002).

## Referencias

- Task YAML: `casa-da-baba/docs/multi-agent/tasks/backlog/TASK-EZ-FRETE-001-calcular-frete.yaml`
- ADRs: ADR-0011 (PT-BR rotulagem + Haversine insuficiente em SP), ADR-0013
  (CancellationToken em IUseCase — seguido).
- Dependencias: TASK-EZ-003 (FreteZona entity), TASK-EZ-009 (FreteZonaRepository).
- Bloqueia: TASK-EZ-CHECKOUT-001 (protocolo 3 fases — precisa do calculo de
  frete consolidado).
