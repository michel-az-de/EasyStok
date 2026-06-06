# 00 — Reconhecimento

> Parte do [Plano Caixa Conciliado + Pagamentos Múltiplos](README.md). Anterior: nenhum. Próximo: [01-dominio.md](01-dominio.md).

### A.1 Stack identificado

- **Linguagem/Framework**: C# / .NET 9.0 (`net9.0` em todos `.csproj` raiz;
  app version 1.10.0 em `EasyStock.Api/EasyStock.Api.csproj:9`)
- **ORM**: Entity Framework Core 9.0.4 + `Npgsql.EntityFrameworkCore.PostgreSQL`
  (`EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj:18`)
- **Banco transacional**: PostgreSQL único. MongoDB descontinuado como
  provider transacional (ver `docs/adr/0001-mongo-discarded.md`)
- **Camadas (Clean Architecture)**:
  - `EasyStock.Domain` — entidades, value objects, exceções, máquinas de estado
  - `EasyStock.Application` — UseCases, ports (IRepository, ICurrentUserAccessor), FluentValidation
  - `EasyStock.Infra.Postgre` — DbContext, Repositories, Migrations, Interceptors
  - `EasyStock.Infra.Notifications` — canais (SMTP, SendGrid, SMS, WhatsApp, WebPush)
  - `EasyStock.Infra.Integrations` — NFe Focus, webhooks
  - `EasyStock.Infra.Async` — workers, PDF render (QuestPDF), reporting
  - `EasyStock.Api` — controllers, middleware (`IdempotencyMiddleware`),
    JWT auth, exception handler
  - `EasyStock.Web` — Razor Pages admin (Tailwind)
  - `EasyStock.Worker` — HostedServices (SLA, agendamento, outbox, reporting, NFC-e)
  - `EasyStok.Mobile` — MAUI Android
  - `EasyStock.Contracts` — DTOs compartilhados
  - Tests: `Domain.Tests`, `Application.Tests`, `Api.UnitTests`,
    `Api.IntegrationTests`, `Infra.Postgre.IntegrationTests`,
    `ArchitectureTests` (NetArchTest), `Benchmarks`
- **Frontend principal**: PWA dentro de `EasyStock.Api/wwwroot/pwa/` (JS
  vanilla + Tailwind). NÃO usa Blazor.
- **Padrão de UseCase**: classe sealed, ctor com dependências injetadas,
  método `ExecuteAsync(Command)` com `Result?`. Sem MediatR. Validação via
  `UseCaseGuards` + atributos `[Required]/[MaxLength]` em records de command.
  Exemplo: `EasyStock.Application/UseCases/RegistrarPagamentoPedido/RegistrarPagamentoPedidoUseCase.cs:22-127`
- **Erros**: exceções custom em `EasyStock.Domain/Exceptions/` (`RegraDeDominioVioladaException`,
  `TransicaoInvalidaException`, `PlanoLimiteAtingidoException` etc.) +
  `UseCaseValidationException` no Application. Global exception handler
  converte para ProblemDetails RFC 7807.
- **Multi-tenancy**: `EmpresaId` (não "LojaId") é o tenant primário.
  `LojaId` é nullable em quase tudo (entidade pode pertencer à empresa sem
  loja específica). Aplicado via `ICurrentUserAccessor.EmpresaId` lido de
  claim JWT `empresaId`. Query filters EF aplicados no DbContext. RLS Postgres
  existe em branch `feat/security-rls-postgres` (não em master).
- **PK**: `Guid` (EF Core gera v7 por default em .NET 9). Todas as entidades
  do domain usam `public Guid Id { get; set; }` (ex: `Pedido.cs:34`,
  `MovimentoCaixa.cs:20`).
- **Money**: dois padrões coexistem. `Pedido.Total` usa `Dinheiro` VO
  (`EasyStock.Domain/ValueObjects/Dinheiro.cs`); `MovimentoCaixa.Valor`,
  `FechamentoCaixa.*`, `PedidoPagamento.Valor`, `Venda.ValorTotal` usam
  `decimal` direto. Postgres column: `numeric(14,2)`. Decisão para módulos
  novos: **usar `decimal` puro** (uniformidade com tabelas pares — todas as
  agregações de caixa já são `decimal`; introduzir `Dinheiro` aqui exigiria
  conversor + quebra de queries de agregação existentes).
- **Migrations**: EF Core code-first. Naming `yyyyMMddHHmmss_PascalCase.cs`
  em `EasyStock.Infra.Postgre/Migrations/`. >130 migrations desde
  `20260403222148_InitialCreate.cs`. 3 mais recentes:
  `20260516014449_AddWebPushSubscription.cs`,
  `20260516010000_UniqueAberturaCaixaPorDia.cs`,
  `20260515230548_AddPedidoIdToAdminTickets.cs`.
- **Convenção nomenclatura**: **PT-BR predomina** no Domain.
  Provas (5 entidades): `Pedido.cs`, `Produto.cs`, `Venda.cs`, `Cliente.cs`,
  `MovimentoCaixa.cs`. Properties em PT-BR (`Nome`, `Email`, `CriadoEm`,
  `AlteradoEm`, `Status`, `Total`, `Tipo`, `Valor`). Status legacy é
  string lowercase (ex: `"aguardando"`). Snake_case nas colunas Postgres.
- **Testes**: xUnit 2.9.2 + NSubstitute 5.x. Coverlet sem threshold
  versionado. NetArchTest valida: Domain sem dependência de
  Application/Infra/Api (ver `EasyStock.ArchitectureTests/ArchitectureTests.cs`).
- **PDF**: QuestPDF Community (`EasyStock.Infra.Async/Pdf/FaturaPdfRenderer.cs:23-42`).
  Template já em uso para faturas SaaS. Reusar pattern.
- **Eventos**: `IPublicadorEventos` (`EasyStock.Application/Ports/Output/Events/IPublicadorEventos.cs:5-8`),
  implementação stub `PublicadorEventosEmMemoria` (`EasyStock.Infra.Postgre/Events/PublicadorEventosEmMemoria.cs`)
  apenas loga. `DomainEvent` abstract record existe em
  `EasyStock.Domain/Events/DomainEvent.cs:5`. **Eventos NÃO são despachados
  para handlers** hoje — log only.
- **Outbox**: `OutboxEventoIntegracao` (`EasyStock.Domain/Integration/OutboxEventoIntegracao.cs:42-203`)
  + `IntegrationOutboxBackgroundService` no Worker. Transactional outbox
  completo com SHA-256, retry com backoff exponencial, sharding (4 shards
  por `aggregate_id % 4`). Usado para integração externa (webhook, NFe).
- **Idempotency**: `IdempotencyMiddleware` (`EasyStock.Api/Middleware/IdempotencyMiddleware.cs:21-251`).
  Header `Idempotency-Key`, 24h TTL, cache em DB com response JSON até 64KB.
  Whitelist por path prefix via `IdempotencyOptions.Add()`.
- **Storage**: `IFileStorage` com 2 implementações: `LocalFileStorage`
  (Fly/VM volume) e `S3CompatibleFileStorage` (R2/MinIO self-hosted). Suporta
  upload, download, pre-signed URLs.
- **Email**: `IEmailService` com `SmtpEmailService` (retry 3x backoff 2s) e
  `SendGridEmailService` (sandbox mode). Switch por config `Email:Provider`.
- **Auditoria**: `AuditLog` (logins, ops sensíveis), `EntityAlteracao` com
  retention 1825 dias = 5 anos via `EntityAlteracaoRetentionService`.
  **5 anos casa com requisito regulatório de FechamentoCaixa.**
- **Worker HostedServices** (`EasyStock.Worker/Program.cs`):
  `SlaMonitorService`, `AgendamentoNotificacaoService`,
  `EndpointHealthMonitorService`, `IntegrationOutboxBackgroundService`,
  `ReportRunnerBackgroundService`, `ReportWatchdogBackgroundService`,
  `ReprocessarContingenciaBackgroundService`, `RenovacaoCertificadoA1BackgroundService`.

### A.2 Estado atual de Pedido

**Arquivo**: `EasyStock.Domain/Entities/Pedido.cs:32-197`

Campos relevantes:
- `Id: Guid` (34), `EmpresaId: Guid` (35), `LojaId: Guid?` (36)
- `ClienteId: Guid?` + snapshot `ClienteNome/Apt/Telefone` (39-45)
- `Status: string` (56) — legacy lowercase: `"aguardando"|"preparando"|"pronto"|"entregue"|"cancelado"`
- `StatusEnum: StatusPedido` (computed, lança se string inválida) (64)
- `Total: Dinheiro` (79) — value object com conversor EF para `numeric(14,2)`
- `Origem: string?` (84), `MobileOrderId: string?` (90)
- `VendaId: Guid?` (96), `Venda: Venda?` (97)
- `CriadoEm`, `AlteradoEm`, `EntreguEm`, `CanceladoEm`, `AgendadoParaEm` (99-109)
- Coleções: `Itens` (PedidoItem), `Eventos` (PedidoEvento), `Pagamentos` (PedidoPagamento) (115-117)
- `TotalPago: decimal` computed (188-196) — `sum(Pagamentos.Valor)`

**Não existe**: `FormaPagamento` na raiz, `Pago` boolean, `PagoEm` na raiz,
`CaixaId` (FK direta). Pagamento é 100% via coleção.

### A.3 Estado atual de Pagamento (`PedidoPagamento`)

**Arquivo**: `EasyStock.Domain/Entities/Pedido.cs:258-276`
**Tabela**: `pedido_pagamentos` (config em `EasyStock.Infra.Postgre/Data/Configurations/PedidoConfiguration.cs`)

Campos:
- `Id: Guid`, `PedidoId: Guid`
- `Metodo: string` — `"pix"|"dinheiro"|"credito"|"debito"|"transferencia"|"outro"`
- `Valor: decimal` — `numeric(14,2)`
- `Referencia: string?` (txid PIX, NSU cartão) — max 120
- `Observacao: string?`
- `PagoEm: DateTime`
- `RegistradoPorUserId: Guid?`, `RegistradoPorNome: string?` — max 120

**Lacunas críticas no schema atual**:
- Sem `Status` (confirmado/estornado/falhou) — append-only, sem ciclo de vida
- Sem `EstornadoEm`, `EstornadoPorUserId`, `MotivoEstorno`, `PagamentoOriginalId`
- Sem `ConciliacaoTipo` (físico vs adquirente vs não-conciliável)
- Sem `MovimentoCaixaId` (link forte com caixa)
- Sem `EmpresaId` denormalizado (precisa join com Pedido para queries de tenant)

### A.4 Estado atual de Caixa

**Arquivo**: `EasyStock.Domain/Entities/MovimentoCaixa.cs:18-148`

Já existem **2 entidades**:

1. **`MovimentoCaixa`** (`MovimentoCaixa.cs:18-94`)
   - Tipos: `"abertura"|"fechamento"|"entrada"|"saida"`
   - `Valor: decimal` sempre positivo, sinal vem de `Tipo` via
     `SinalNoCaixa` (86-93)
   - Suporta estorno: `EstornadoEm`, `EstornadoPorUserId`, `EstornadoPorNome`,
     `MotivoEstorno` (47-50, 77-83)
   - `Metodo`, `Categoria`, `Referencia`, `DataMovimento` (pode ser retroativo)
   - `RegistradoPorUserId`, `RegistradoPorNome`, `Origem`
   - **Fato chave**: comentário XML linha 15-16 declara "Pagamentos de pedido
     NÃO viram MovimentoCaixa — ficam em PedidoPagamento". Hoje pagamento e
     caixa são tracks paralelas reconciliadas só por agregação. Isso vai
     mudar.

2. **`FechamentoCaixa`** (`MovimentoCaixa.cs:102-148`)
   - Snapshot consolidado por `(EmpresaId, LojaId, Data: DateOnly)`
   - Unique index em `(EmpresaId, LojaId, Data)` (config:51)
   - Fields: `SaldoInicial`, `TotalVendas`, `TotalPagamentosPedidos`,
     `TotalEntradasExtras`, `TotalSaidasExtras`, `SaldoFinal` (calculado em
     `Criar` 144-145)
   - `FechadoPorUserId`, `FechadoPorNome`, `Observacoes`, `FechadoEm`
   - **Lacunas**: sem hash, sem snapshot detalhado (jsonb), sem conferência
     física, sem divergências, sem PDF persistido, sem QR pública

**Não existe**: `SessaoCaixa` (entidade representando período aberto).
"Caixa aberto" hoje é detectado por query: "existe `MovimentoCaixa` tipo
`abertura` no dia X sem `fechamento` correspondente?". Implementação:
`CaixaRepository.GetMovimentosDoDiaAsync` + filtro em UseCase.

**Constraint recente importante**: migration
`20260516010000_UniqueAberturaCaixaPorDia.cs` adicionou índice único parcial
para impedir duas aberturas no mesmo dia/empresa/loja. **Reusar essa
proteção** em SessaoCaixa.

### A.5 Onde Pedido é marcado como pago HOJE

Não existe campo `Pago` nem `MarcarComoPago`. Estado de pagamento é
**inteiramente derivado** de `pedido.TotalPago` vs `pedido.Total`.

UseCases que mexem em `PedidoPagamento`:
1. `RegistrarPagamentoPedidoUseCase` (`EasyStock.Application/UseCases/RegistrarPagamentoPedido/RegistrarPagamentoPedidoUseCase.cs:33-127`)
   - Insere `PedidoPagamento` + evento `"pagamento"` + tenta abrir caixa
   - **Não valida `pedido.TotalPago + cmd.Valor <= pedido.Total`** → permite excedente
   - Side effect: `TentarAbrirCaixaAsync` cria `MovimentoCaixa` tipo abertura
     se não existe ainda no dia. **Não cria `MovimentoCaixa` por pagamento**.
   - Transação: sim, via `IUnitOfWork.CommitAsync()`
2. `RemoverPagamentoPedidoUseCase` (`EasyStock.Application/UseCases/RemoverPagamentoPedido/RemoverPagamentoPedidoUseCase.cs:23-54`)
   - **DELETE físico** + evento `"pagamento_removido"`
   - Nenhum motivo obrigatório, nenhum movimento reverso
   - **GAP CRÍTICO: viola auditoria fiscal**
3. `CancelarPedidoUseCase` (`EasyStock.Application/UseCases/CancelarPedido/CancelarPedidoUseCase.cs:25-56`)
   - Muda status via `pedido.Cancelar()` (state machine)
   - **NÃO estorna pagamentos** — pedido cancelado mantém PedidoPagamento ativos
   - **GAP CRÍTICO**

### A.6 Acoplamentos perigosos identificados

| # | Acoplamento | Local | Gravidade | Ação no plano |
|---|---|---|---|---|
| 1 | `RemoverPagamentoPedidoUseCase` DELETE físico sem audit | `RemoverPagamentoPedido/...cs:23-54` | **ALTA** | Endpoint mantido como deprecated (HTTP 410 após 1 release); substituído por `EstornarPagamentoUseCase` |
| 2 | `CancelarPedidoUseCase` não estorna pagamentos | `CancelarPedido/...cs:25-56` | **ALTA** | Adicionar estorno em cascata + transição `cancelado_com_estorno_pendente` → `cancelado_estornado` |
| 3 | Abertura automática de caixa em `try/catch` engolido | `RegistrarPagamentoPedido/...cs:120-125` | **MÉDIA** | Mantida (UX já estabelecida) mas estendida: criar `SessaoCaixa` + `MovimentoCaixa` linkado |
| 4 | `Pedido.TotalPago` ignora status do pagamento | `Pedido.cs:188-196` | **MÉDIA** | Após M5, computed filtra `Status == "confirmado"` |
| 5 | `GetTotalPagamentosPedidosDoDiaAsync` filtra por `Status != "cancelado"` mas não checa pagamento estornado | `CaixaRepository.cs:91-109` | **MÉDIA** | Adicionar filtro `pg.Status == "confirmado"` |
| 6 | Status `"cancelado"` é string mágica espalhada (Pedido + Repo + UseCases) | múltiplos | **BAIXA** | Sem ação — refactor fora de escopo |

### A.7 Componentes reusáveis (NÃO recriar)

| Componente | Path | Reuso |
|---|---|---|
| `IdempotencyMiddleware` + `IdempotencyOptions` | `EasyStock.Api/Middleware/IdempotencyMiddleware.cs` | Adicionar paths `/api/pedidos/{id}/pagamentos`, `/api/pagamentos/{id}/estornar`, `/api/caixa/sessoes/*` à whitelist |
| `IFileStorage` (Local/S3/Azure) | `EasyStock.Application/Ports/Output/Storage/IFileStorage.cs` | Persistir PDF do fechamento + recuperar para download/verificação |
| `IEmailService` (SMTP/SendGrid) | `EasyStock.Infra.Async/SmtpEmailService.cs`, `SendGridEmailService.cs` | Envio opcional do PDF ao contador |
| `FaturaPdfRenderer` (QuestPDF pattern) | `EasyStock.Infra.Async/Pdf/FaturaPdfRenderer.cs` | Modelo para `FechamentoCaixaPdfRenderer` |
| `OutboxEventoIntegracao` + `IntegrationOutboxBackgroundService` | `EasyStock.Domain/Integration/OutboxEventoIntegracao.cs` + `EasyStock.Worker/.../IntegrationOutboxBackgroundService.cs` | Eventos `pagamento.confirmado`, `caixa.fechado` para webhook/email |
| `CaixaRepository.Get*` agregações | `EasyStock.Infra.Postgre/Repositories/CaixaRepository.cs` | Reusar para calcular snapshot de fechamento |
| `EntityAlteracao` retention 1825 dias | `EasyStock.Infra.Postgre/Hosting/EntityAlteracaoRetentionService.cs` | Já cobre 5 anos regulatório para `FechamentoCaixa` |
| `UseCaseGuards` + `UseCaseValidationException` | `EasyStock.Application/UseCases/Common/` | Validações server-side dos novos UseCases |
| `ICurrentUserAccessor` | `EasyStock.Application/Ports/Output/ICurrentUserAccessor.cs` | EmpresaId + UserId em todos os novos UseCases |
| `PedidoStateMachine` pattern | `EasyStock.Domain/Sales/PedidoStateMachine.cs` | Modelo para `SessaoCaixaStateMachine` |
| `IPublicadorEventos` | `EasyStock.Application/Ports/Output/Events/IPublicadorEventos.cs` | Eventos in-process (mas eventos críticos vão pelo outbox) |
| SHA-256 padrão em `OutboxEventoIntegracao` linha 200 | `EasyStock.Domain/Integration/OutboxEventoIntegracao.cs:200` | Pattern para hash do FechamentoCaixa |

### A.8 Lacunas (construir do zero)

- **Entidade `SessaoCaixa`** (tabela + entity + config + state machine + repository + DbSet)
- **Colunas aditivas em `PedidoPagamento`**: `Status`, `EstornadoEm`,
  `EstornadoPorUserId`, `EstornadoPorNome`, `MotivoEstorno`,
  `PagamentoOriginalId`, `ConciliacaoTipo`, `MovimentoCaixaId`,
  `EmpresaId` (denorm)
- **Colunas aditivas em `MovimentoCaixa`**: `SessaoCaixaId` (FK opcional),
  `PagamentoId` (FK opcional para pagamento que gerou)
- **Colunas aditivas em `FechamentoCaixa`**: `SessaoCaixaId`, `HashSha256`,
  `PdfStorageKey`, `Snapshot` (jsonb), `ConferenciaItens` (jsonb),
  `Divergencias` (jsonb), `VerificacaoCodigo` (slug opaco), `EmailContadorEnviadoEm`
- **UseCases novos**: `ConfirmarPagamentoUseCase`, `EstornarPagamentoUseCase`,
  `ListarPagamentosPedidoUseCase`, `AbrirSessaoCaixaUseCase`,
  `RegistrarMovimentoManualUseCase`, `IniciarFechamentoSessaoUseCase`,
  `ConfirmarFechamentoSessaoUseCase`, `ListarSessoesCaixaUseCase`,
  `GerarPdfFechamentoUseCase`, `VerificarFechamentoPublicoUseCase`,
  `EstornarPagamentosCancelamentoHandler` (handler interno)
- **Controllers**: `PagamentosController` (estorno endpoint), expansão de
  `CaixaController` (sessões, wizard, PDF, verificação pública)
- **PDF renderer**: `FechamentoCaixaPdfRenderer` em `EasyStock.Infra.Async/Pdf/`
- **PWA screens**: aba Pagamentos no pedido (refactor), tela Caixa,
  wizard de fechamento 3 passos, tela de verificação pública (rota anônima)
- **Eventos de domínio**: `PagamentoConfirmado`, `PagamentoEstornado`,
  `SessaoCaixaIniciandoFechamento`, `SessaoCaixaFechada`
- **Migrations**: 5 migrations (esquema, índices concurrent, backfill,
  rename de endpoint deprecated, observabilidade)

### A.9 Riscos descobertos (não previstos no escopo original)

- **R-D1: Mobile MAUI usa `PedidoPagamento` via sync** (memory: APK Casa da
  Babá, polling 30s). Mudar contrato pode quebrar sync → preservar
  `PedidoPagamento` físico, só adicionar colunas com defaults.
- **R-D2: PWA já lê `pedido.Pagamentos[]` direto** em telas existentes
  (memory: P-01 refatoração tela produtos não tocou pedidos). Filtrar
  `Status == "confirmado"` no DTO mapper pode esconder dado de bug — UseCases
  precisam retornar coleção filtrada por default + endpoint para "incluir
  estornados" via querystring.
- **R-D3: NFC-e F0 100% (memory)** já depende de `Pedido` e seus pagamentos.
  Backfill precisa garantir que pedidos com NFC-e emitida não tenham
  pagamento status alterado retroativamente — só `confirmado` para tudo
  histórico, sem reconciliação retroativa.
- **R-D4: RLS Postgres em branch separado** (`feat/security-rls-postgres`,
  CI bloqueado por billing — memory). Plano deve assumir multi-tenancy
  application-level (query filters). Se RLS chegar primeiro, adicionar
  policies para novas tabelas no PR de RLS.
- **R-D5: GitHub Actions parado por billing** (memory 2026-05-11). CI roda
  pre-commit local, testes manuais antes do deploy via fly. Buffer maior no
  cronograma (R8).
- **R-D6: `EntityAlteracaoRetentionService` precisa registrar `SessaoCaixa`
  e `FechamentoCaixa` com retention 1825 dias** explicitamente — não basta
  default, configurar.
- **R-D7: Casa da Babá hoje "fecha caixa" via UseCase atual** (memory
  resumo_horario_conventions). Deploy precisa ser feature-flagged por
  empresa para coexistência durante validação.

---
