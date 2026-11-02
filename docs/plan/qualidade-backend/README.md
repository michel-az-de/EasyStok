# Qualidade do backend: levantamento e guardas de risco

- Data: 2026-11-01
- Base medida: `origin/master` `e26cb4025`
- Épico: #2171. Plano: #2172.
- Status: aprovado pelo Felipe em 2026-11-01. Executar primeiro as guardas de risco (S1 a S4). Achados de severidade média e baixa ficam registrados aqui, sem execução agora.

## 1. O que falta para a jornada mínima

A jornada mínima (#1889) não está parada por código que falta. Está parada por itens de produção.

| Frente | Estado em 01/11 | Dono |
|---|---|---|
| Produção | master 20+ commits à frente do último deploy (`4f9304242`, 27/10), com 2 migrações | Agente |
| Pagamento real de R$ 1 com estorno; webhook de teste do Mercado Pago (#2101) | Pendente | Felipe |
| `/health/notificacoes` em 503 | Falta a chave de canal desligado no compose | Agente |
| WhatsApp perde mensagem de cliente com LID (#2118); #2075 com gitleaks vermelho | p1 | Agente |
| Caixa: #1630, #950, #640 | p1 da jornada | Agente |
| Meta App Review | Rascunho; faltam vídeos e ícone 1024 | Felipe |
| Backlog | 249 issues abertas (6 p0, 80 p1); 174 paradas desde antes de 01/10; F4 da #1889 pela metade | Triagem |

No backend: zero `NotImplementedException`, zero FIXME/HACK, um TODO real (S27, abrir Ocorrência na avaliação negativa, em `AvaliacaoAcaoBotao.cs` e `RegistrarAvaliacaoFerramenta.cs`).

## 2. Achados do code review

Números de `git grep` sobre o `origin/master`; os que dependem de regex são aproximados. O que o `EasyStock.ArchitectureTests` já garante não entra como achado: direção das camadas, Application sem EF/AspNetCore, proibição de `DateTime.Now`/`Today`, cobertura de RLS por tabela, allowlist de quem desliga RLS, higiene de testes e dono único de fuso, CEP e senha.

### Alta

| ID | Achado | Evidência | Frente |
|---|---|---|---|
| A1 | Módulo Mobile sem filtro global de tenant e sem RLS | `EasyStockDbContext.cs` (`SkipTenantFilter`) isenta `Entities.Mobile`; `CoberturaRlsTests.cs` isenta `mobile_`; ~32 tabelas por SQL cru em `EasyStock.Api/Mobile/Schema`; isolamento por `Where` manual em 12 controllers; `OrderLinker.cs` com 22 `IgnoreQueryFilters`; vazamento já registrado no #1509 | S4 |
| A2 | `Api.IntegrationTests` (227 testes) fica verde se o Postgres não subir | `PostgresApiIntegrationTests.cs` marca `_isAvailable=false` na falha; 47 arquivos com `Skip.If(!_isAvailable)`; não há canário como `HarnessCanaryTests` | S1 |
| A3 | Pagamento, frete e IA sem pipeline de resiliência | `MercadoPagoClient`, `GoogleRotasClient`, `TomTom*`, `AnthropicMessagesClient` não usam `ResiliencePipelineProvider`; categorias `Payments`, `Fiscal`, `Marketplace` registradas sem uso | S2 |
| A4 | Máquina de estados do Pedido contornável | `Pedido.cs:61` `public string Status { get; set; }`; atribuição direta em `CheckoutCoreService.cs` (3), `AprovarPedidoStorefrontUseCase.cs`, `RecusarPedidoStorefrontUseCase.cs`, `IniciarCheckoutGuestUseCase.cs`, `CancelarPedidosAbandonadosBackgroundService.cs` e 2 seeders/importadores da Api | S3 |
| A5 | Total e subtotal recalculados fora do agregado | `CheckoutCoreService.cs` (total e `Subtotal = Qtd * preço` sem arredondar), `GerarCobrancaPedidoUseCase.cs`, `ComandaClienteUseCase.cs` (2), `ConfirmarAlteracaoComandaUseCase.cs` | Próxima rodada |
| A6 | Classes gigantes e use case injetando use case | `ComandaClienteUseCase.cs` 930 linhas, 27 dependências, 13 tipos; `ProcessarEventoWhatsAppUseCase.cs` 623 linhas, 23 dependências; 126 injeções de `*UseCase` concreto em 79 arquivos; 58 dependências opcionais `= null` | Próxima rodada |
| A7 | CancellationToken pela metade, contra o ADR-0013 | 197 de 412 `ExecuteAsync` recebem CT; 489 de 953 métodos async das Ports; 164 `SaveChangesAsync()`/`ToListAsync()` sem token na Infra | Próxima rodada (ADR novo) |

### Média

| ID | Achado | Evidência |
|---|---|---|
| M1 | Domínio anêmico no núcleo antigo | 1.864 setters públicos contra 640 privados; Cliente 80, Pedido 79, Lote 49, ItemEstoque 40 sem `private set`. `Conversa.cs` é o modelo a seguir |
| M2 | Contrato de use case fragmentado | 38 de 458 `*UseCase` implementam `IUseCase`; 762 `throw new UseCaseValidationException` contra 13 `AbstractValidator`; 75 arquivos com 5+ tipos de topo |
| M3 | Relógio direto | `DateTime.UtcNow`: 349 no Domain, 192 na Application, 129 na Api, 71 na Infra.Postgre; a guarda não cobre `UtcNow` |
| M4 | Pastas por verbo misturadas com feature | 13 pastas de Pedido em `UseCases`; `UseCases/Pedido` guarda PedidoFornecedor; dois `IClienteRepository` |
| M5 | Status em string e modelo Mobile paralelo | 47 propriedades string de status/tipo; `Entities/Mobile` duplica Pedido, Produto, Lote e Cliente em inglês; o Mobile soma 8.613 linhas da Api, com schema fora das migrações do EF |
| M6 | Mensagem de exceção e corpo de provedor expostos | `IaAutoPreenchimentoController.cs` manda `ex.Message` por SSE; `EfiPixService.cs` loga corpo cru (o Mercado Pago já usa `CorpoDeProvedor.ParaRegistro`) |
| M7 | Réplica única não declarada | 24 BackgroundServices no host da Api; fila de mídia e SSE em memória; outbox sem `SKIP LOCKED` |
| M8 | Cinco formatos de erro e catch repetido | 223 `DataBadRequest`, 38 `Problem`, 17 `{error}`, 2 `{erro}`, 4 `{message}`; 97 `catch (UseCaseValidationException)` que o `GlobalExceptionHandler` já mapeia |
| M9 | Dívida de arquitetura fora do gate | `ci.yml` filtra `Category!=ArchitectureDebt`; `Program.cs` com 244 linhas para limite de 200; 14 controllers com DbContext |

### Baixa

- Advisory lock copiado em `CobrancaPedidoJob`, `ContaFinanceiraVencimentoJob` e `ContaReceberPixReconciliacaoJob` em vez de `PostgresAdvisoryLock`.
- Verificação de assinatura de webhook repetida em 5 controllers; token do WPPConnect na query.
- Worker sem projeto de teste; `ReportRunnerBackgroundService` com 502 linhas e 8 `catch (Exception)`.
- Cobertura só no job noturno, piso global de 38% e nenhum piso para Postgre, Integrations e Worker.
- Exceções de infra no Domain (`MercadoPagoIndisponivelException`, `OtpProviderException`); `Microsoft.Extensions.Caching.Memory` referenciado na Application.

### Pontos fortes a preservar

1. Guardas executáveis com allowlist que só diminui (`AmbientClockBanTests`, `RegraUnicaNoBackendTests`, `HarnessCanaryTests`, `TestHygieneTests`). As specs abaixo seguem esse mecanismo.
2. Multi-tenant em duas camadas no núcleo: filtro do EF mais RLS via `SetTenantOnConnectionInterceptor`; nenhum SQL cru interpolado.
3. `ModulosConvention` derruba a subida com controller sem classificação; 127 controllers com `[Authorize]` ou `[AllowAnonymous]` explícito; webhooks com HMAC em tempo constante.
4. Zero `async void`, zero `#pragma warning disable`, zero log interpolado; namespaces 100% alinhados com as pastas.

## 3. Specs das guardas de risco

Cada spec vira uma issue filha do #2171, worktree próprio, Red antes do Green, gate local e build Release.

### S1. Canário do Api.IntegrationTests (A2, `test`, tier baixo)

- Teste em `EasyStock.Api.IntegrationTests` que falha quando `GITHUB_ACTIONS=true` e o Postgres do Testcontainers não subiu, no desenho de `EasyStock.Infra.Postgre.IntegrationTests/HarnessCanaryTests.cs`.
- Reaproveita o ponto de disponibilidade de `PostgresApiIntegrationTests.cs`; não duplica a subida do container.
- Aceite: canário vermelho com container indisponível e `GITHUB_ACTIONS=true`; verde no job `api-integration`.

### S2. Resiliência em pagamento, frete e IA (A3, `fix`, tier baixo)

- Usa o registro existente em `Infra.Integrations/DependencyInjection/IntegrationsServiceCollectionExtensions.cs` e o padrão de `MetaMensageriaHttpTransporte.cs`.
- Leitura (GET) com retry; escrita (POST/PUT de cobrança, estorno, mensagem de IA) só com disjuntor e timeout, porque não é idempotente.
- Fora: Lalamove e `Logistica/**`, que têm sessão própria. Sem pacote novo em `*.csproj`.
- Aceite: teste com handler falso (503, 503, 200) em que o GET recupera e o POST não repete.

### S3. Trava de `Pedido.Status` fora do Domain (A4, refactor, tier baixo por esta spec)

- Teste de arquitetura no estilo `AmbientClockBanTests`: proíbe atribuir `Pedido.Status` fora de `EasyStock.Domain`, com allowlist dos pontos atuais que só diminui.
- Os pontos do checkout e da aprovação passam a chamar métodos do agregado que validam a transição pela `PedidoStateMachine`. O job de abandonados usa o cancelamento do Domain.
- `PedidoFornecedor` é outra entidade e fica fora. Seeders e importador legado do Mobile ficam na allowlist.
- Sem mudança de comportamento: os testes de Application do checkout, da aprovação e da recusa continuam verdes sem edição de asserção.
- Aceite: allowlist com no máximo os seeders e o importador; teste de arquitetura verde.

### S4. RLS nas tabelas `mobile_*` (A1, migração e RLS, tier alto)

- Antes de codar: medir quais `mobile_*` têm `empresa_id`, se todos os caminhos do Mobile emitem `app.empresa_id` e quais fluxos leem entre empresas de propósito.
- Red: teste com duas empresas lendo `mobile_*` sem bypass, que hoje vaza.
- Green: migração com `ENABLE ROW LEVEL SECURITY` e policy `tenant_isolation` igual às do núcleo; `mobile_` sai da isenção de `CoberturaRlsTests`. O `Down` desliga as policies.
- Risco: quebrar o sync do APK. Validação no laboratório com o APK no emulador antes do merge.
- Aceite: label `aprovado`, testes de RLS e do Mobile verdes.

## 4. Próxima rodada (sem execução agora)

Ordem sugerida quando as guardas fecharem: A5 (total dentro do agregado), A7 (ADR que substitui o 0013 e guarda de CT), M3 (estender a guarda de relógio a `UtcNow`), M8 (um formato de erro e remoção dos catches repetidos), A6 (dividir a Comanda por grupo de operação).
