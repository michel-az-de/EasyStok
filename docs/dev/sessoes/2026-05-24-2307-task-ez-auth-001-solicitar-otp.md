# Sessao TASK-EZ-AUTH-001 - solicitar OTP via WhatsApp/SMS

Data: 2026-05-24 23:07 UTC
Worktree: C:/easy/EasyStok/.claude/worktrees/wt-ez-auth-001
Branch: feat/task-ez-auth-001-solicitar-otp (5 commits ahead da base ez-009)
Identidade Git: felipe.azevedo@gmail.com / gh michel-az-de
Status final: completo (aguardando revisao + merge na cadeia ez-001..ez-009 + push)

## O que foi feito

Implementacao completa do use case do primeiro passo do fluxo de autenticacao
do storefront (ADR-0012):

POST `/api/storefront/{slug}/auth/solicitar-otp`

Recebe `{telefone}` no body + header opcional `X-Idempotency-Key`. Em sucesso
retorna `202 { expiresInSeconds: 300 }`. Mapeamento de erros:

- 400 `TelefoneInvalidoException` (formato nao bate E.164 BR);
- 404 `StorefrontNaoEncontradoException` (slug inexistente ou storefront inativo);
- 429 `OtpRateLimitExcedidoException` com body `{retryAfterSeconds}` + header
  `Retry-After` (cota 3 OTPs/hora por telefone);
- 502 `OtpProviderException` (provider WhatsApp falhou — OTP persistido para
  Reenviar).

Componentes entregues:

**Domain (4 exceptions):**
- `TelefoneInvalidoException`
- `OtpRateLimitExcedidoException` (com `RetryAfterSeconds`)
- `StorefrontNaoEncontradoException`
- `OtpProviderException`

**Application:**
- `IWhatsAppOtpSender` (port em `Ports/Output/Messaging/`)
- `SolicitarOtpInput` / `SolicitarOtpResult` (records)
- `SolicitarOtpUseCase` (TDD red-green com 17 metodos de teste = 24 com Theory)
- `IClienteOtpRepository.ContarCriadosDesdeAsync` (rate limit query)
- `ServiceCollectionExtensions.Storefront.cs` (partial DI)

**Infra:**
- `ClienteOtpRepository.ContarCriadosDesdeAsync` (impl EF)
- `EasyStock.Infra.Integrations/WhatsApp/StubWhatsAppOtpSender.cs`
  (loga codigo via LogDebug em Development; lanca em Production via guarda no ctor)
- `WhatsAppServiceCollectionExtensions.AddEasyStockWhatsAppStub`

**Api:**
- `EasyStock.Api/Controllers/Storefront/AuthController.cs`
- `Program.cs` chama o stub se `!IsProduction()`

**TestHelpers:**
- `FakeTimeProvider` (publico, reusavel — generaliza o `FakeTime` internal do
  Domain.Tests)

**Tests:**
- `SolicitarOtpUseCaseTests.cs` (Application.Tests, 24/24 PASS)
- `AuthControllerTests.cs` (Api.IntegrationTests, 3 cenarios — Testcontainers
  Postgres, no-op se Docker indisponivel)

## Decisoes tomadas (divergencias documentadas vs YAML)

1. **expiresInSeconds = 300 (5min), nao 600 (10min)**: a entity `ClienteOtp`
   (TASK-EZ-005) define `TempoVidaPadrao = 5min`. Seguimos a entity como
   fonte da verdade — YAML EZ-AUTH-001 estava com texto desatualizado.

2. **StubWhatsAppOtpSender em `Infra.Integrations/WhatsApp/`, nao em projeto
   novo `Infra.WhatsApp`**: segue convencao do repo (todos os adapters Polly
   + Fiscal + Pagamentos vivem em Infra.Integrations). Evita criar csproj
   adicional + entrada no .sln. Reversivel se um provider WhatsApp mais
   complexo entrar em TASK-EZ-WA-001.

3. **Idempotencia por janela de 60s no `telefoneHash`, sem persistir
   `X-Idempotency-Key`**: cobre o caso de uso real (double-tap do botao
   "Reenviar") sem criar tabela dedicada. A key recebida no header e usada
   apenas como correlation id em logs. Persistencia explicita da key fica
   para iteracao futura se algum novo cenario justificar.

4. **Ordem persist-then-send**: OTP e gravado + commit ANTES do envio
   externo. Se o provider falhar, exceto propaga (502) mas o OTP fica no
   DB — cliente clica "Reenviar" e a janela de 60s reaproveita. Alternativa
   send-then-persist teria pior contrato: codigo enviado sem hash no DB
   impediria validacao.

5. **Em Production sem `IWhatsAppOtpSender` registrado, AuthController nao
   resolve** (fail fast intencional). Comportamento desejado ate
   TASK-EZ-WA-001 entregar o `WhatsAppCloudApiOtpSender` real (apos
   Meta Business Verification — TASK-HUM-001).

6. **CRLF + UTF-8 BOM aplicado APENAS nos 17 arquivos do escopo**. Format
   global do `EasyStok.sln` mostra ~1900 arquivos com erros pre-existentes
   (CHARSET/ENDOFLINE/WHITESPACE) em codigo herdado da branch ez-009 e
   ancestrais. Sob R6 (preservar trabalho de outras sessoes), nao corrigi —
   limpeza global precisa de chore PR dedicado (mesmo padrao do
   `5749e798 chore(TASK-EZ-003): aplicar dotnet format`).

## Quality gates (status)

- `dotnet build EasyStok.sln -warnaserror:nullable -c Debug`: ✅ 0 erros,
  8 warnings (todos pre-existentes — Android Mobile CA1422, ProdutosController
  CS8602, LocalFileStorage CS9107, RLS tests EF1002).
- `dotnet test ArchitectureTests`: ✅ 25/25.
- `dotnet test Application.Tests --filter SolicitarOtpUseCaseTests`:
  ✅ 24/24.
- `dotnet test Application.Tests` (full): ✅ 644/644.
- `dotnet test Api.UnitTests`: ✅ 293/293.
- `dotnet test Api.IntegrationTests --filter AuthControllerTests`:
  ✅ 3/3 (no-op aqui — sem Docker local; rodara em CI).
- `dotnet format --verify-no-changes`: ❌ falha em ~1900 arquivos
  pre-existentes do repo; MEUS arquivos passam (nenhum dos 17 do escopo
  aparece nos errors apos af19ae14). Documentado em divergencia (5).
- Security: codigo nunca em log/response/exception/metrica.
  Unico log do codigo plain e em `StubWhatsAppOtpSender.EnviarOtpAsync`
  (LogDebug + guarda IsProduction no ctor) — comportamento esperado e
  documentado no proprio YAML da task ("Provider stub em Development loga
  codigo no console (DEBUG)").

## Commits criados

- 5fdc6bee `test(TASK-EZ-AUTH-001): red - SolicitarOtpUseCase + 17 cenarios`
- e754309a `feat(TASK-EZ-AUTH-001): green - SolicitarOtpUseCase + rate limit + idempotencia`
- 16835042 `feat(TASK-EZ-AUTH-001): StubWhatsAppOtpSender + AuthController + DI`
- a2965b6d `test(TASK-EZ-AUTH-001): integration test AuthController com Postgres`
- af19ae14 `chore(TASK-EZ-AUTH-001): aplicar CRLF + UTF-8 BOM nos arquivos do escopo`

## Branches criadas/deletadas

- Criada: `feat/task-ez-auth-001-solicitar-otp` (a partir de
  `feat/task-ez-009-repos-storefront`)
- Worktree: `.claude/worktrees/wt-ez-auth-001` (criado, ainda ativo)

## O que ficou pendente

- **Merge da cadeia ez-001..ez-009**: as branches ancestrais
  (`feat/task-ez-001-entity-storefront` ate `feat/task-ez-009-repos-storefront`)
  nao tem PRs abertas. EZ-AUTH-001 esta empilhado em ez-009; o PR de
  EZ-AUTH-001 precisa esperar essa cadeia subir em master primeiro.
- **TASK-EZ-WA-001**: provider WhatsApp real (Meta Cloud API). Bloqueia o
  uso em Production. Depende de TASK-HUM-001 (Meta Business Verification).
- **Push da branch**: requer autorizacao explicita do Felipe (R9 do
  CLAUDE.md).
- **PR**: criar `gh pr create` requer base correta — provavelmente
  `feat/task-ez-009-repos-storefront` ate a cadeia ez-001..ez-009 subir.

## Proxima acao recomendada

1. Felipe decide se quer abrir PR ja (base ez-009) ou esperar merge da cadeia.
2. Felipe autoriza `git push -u origin feat/task-ez-auth-001-solicitar-otp`.
3. Optionalmente, criar chore PR para `dotnet format` global no master
   (limpar os ~1900 arquivos pre-existentes — fora do escopo desta task).
4. Iniciar TASK-EZ-AUTH-002 (validar OTP) — depende desta + ez-005 + ez-009.

## Referencias

- Task YAML: `casa-da-baba/docs/multi-agent/tasks/done/TASK-EZ-AUTH-001-solicitar-otp.yaml`
- ADRs relacionados: ADR-0012 (sessoes server-side storefront),
  ADR-0013 (CancellationToken IUseCase deferred — seguido)
- Dependencias: TASK-EZ-005 (ClienteOtp + ClienteSession entities),
  TASK-EZ-009 (repos), TASK-EZ-008 (migration consolidada)
- Bloqueadores externos: TASK-HUM-001 (Meta Business Verification) bloqueia
  uso em Production via TASK-EZ-WA-001
