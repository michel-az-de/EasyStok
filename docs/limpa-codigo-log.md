# Log de limpeza de código — limpa-codigo

Mantido pela tarefa agendada `limpa-codigo`. Cada entrada registra o que foi encontrado, o que foi corrigido e o que foi deixado intencionalmente.

---

## 2026-05-07 — Commits billing F10–F14 (dashboard financeiro, cache, adapters, webhook, auto-ticket)

### Arquivos analisados (24 arquivos .cs dos commits 74a2b08..5b88609)

| Arquivo | Status |
|---|---|
| `AutoTicketFalhaPagamento.cs` | Limpo — doc completo, sem código morto |
| `MetricasFinanceirasUseCase.cs` | Limpo — doc completo, cache TTL explicado |
| `StripeSignatureValidator.cs` | Limpo — replay protection documentado |
| `MercadoPagoSignatureValidator.cs` | Limpo |
| `EfiPixGatewayAdapter.cs` | Limpo |
| `StripeGatewayAdapter.cs` | Limpo — stub bem documentado com guide de integração |
| `MercadoPagoGatewayAdapter.cs` | Limpo |
| `EfiPixWebhookProcessor.cs` | Limpo |
| `FaturaRepository.cs` | Limpo |
| `AdminFaturasController.cs` | **Fix aplicado** |
| `FaturaReconciliacaoJob.cs` | Limpo |
| `IFalhaPagamentoNotifier.cs` | Limpo |
| `IAssinaturaEmpresaRepository.cs` | **Fix aplicado** |
| `IFaturaRepository.cs` | **Fix aplicado** |
| `IEfiPixService.cs` | Limpo |
| `EfiPixService.cs` | **Fix aplicado** |
| `DashboardModel.cshtml.cs` | Limpo — helpers Dec/Int/Dbl aceitáveis como one-liner |
| `ApiServiceCollectionExtensions.cs` | Limpo |
| `ServiceCollectionExtensions.cs` (Infra.Async) | Limpo |
| `BackgroundJobServiceCollectionExtensions.cs` | **Fix aplicado** |
| `BackgroundJobOptions.cs` | Limpo |
| `ServiceCollectionExtensions.Core.cs` | Limpo |
| `MongoIdentityRepositories.cs` | Limpo |
| `StripeSignatureValidatorTests.cs` | Limpo |
| `AssinaturaEmpresaRepository.cs` | **Fix aplicado** |

### Fixes aplicados

#### 1. `IFaturaRepository.cs` — usings redundantes removidos
- Removidos: `using System;`, `using System.Collections.Generic;`, `using System.Threading;`, `using System.Threading.Tasks;`
- Motivo: projeto tem `<ImplicitUsings>enable</ImplicitUsings>` — todos cobertos por global usings .NET 9.

#### 2. `AdminFaturasController.cs` — using não utilizado removido
- Removido: `using System.Text.Json;`
- Motivo: o controller não referencia nenhum tipo de `System.Text.Json` diretamente; toda serialização passa pelas use cases.

#### 3. `EfiPixService.cs` — código morto removido
- Removida linha: `if (cache.TryGetValue(TokenCacheKey, out string? _) is false) { /* warmup */ }`
- Motivo: bloco vazio que não executava nenhuma ação. O `ObterTokenAsync(ct)` logo abaixo já gerencia o cache corretamente.

#### 4. `BackgroundJobServiceCollectionExtensions.cs` — comentário desatualizado corrigido
- Antes: dizia "NO-OP enquanto adapter Pix retorna Desconhecido (estender IEfiPixService.GetCobrancaAsync em release futura)"
- Depois: "Pix funciona ponta-a-ponta desde F11 (IEfiPixService.ConsultarCobrancaAsync via GET /v2/cob/{txid})"
- Motivo: F11 (commit 9d64666) implementou `ConsultarCobrancaAsync`; o comentário estava descrevendo o estado pré-F11.

#### 5. `IAssinaturaEmpresaRepository.cs` + `AssinaturaEmpresaRepository.cs` — namespace file-scoped
- Ambos usavam block-scoped namespace `namespace ... { }` enquanto os demais arquivos do mesmo projeto (ex: `IFaturaRepository.cs`, `FaturaRepository.cs`) já usavam `namespace ...;`
- Convertidos para file-scoped para consistência.
- Também removido prefixo `EasyStock.Domain.Enums.` desnecessário em `StatusAssinatura` (using já presente).

### Padrões observados (aprendizado para próximas rodadas)

- Billing F10–F14 tem boa cobertura de XML doc; código gerado pela IA já vem com comentários relevantes.
- Stale comments sobre features F-numbered são o principal risco — quando um F-number muda, comentários anteriores ficam desatualizados.
- Adapters stub (Stripe/MP) documentam corretamente seu estado provisório com guide de integração — não tocar.
- `IgnoreQueryFilters()` em queries de métricas/background é intencional — não remover.
- Block-scoped namespace ainda aparece esporadicamente em arquivos mais antigos ou de nova criação sem padrão imposto; converter para file-scoped ao encontrar.

---

## 2026-05-07 (rodada 3) — Pos commit fb4f08e (fully-qualified names + record sealed)

### Escopo
Janela "30min" da tarefa agendada nao tinha commits novos; varredura focou em residuos pos-`fb4f08e` (cleanup billing F12 + Mongo) que nao foram capturados na rodada anterior.

### Fixes aplicados

#### 1. `IEfiPixService.cs` — `record` -> `sealed record`
- `EfiCobrancaResult` declarado como `public record` enquanto `EfiCobrancaStatusResult` e `EfiEstornoResult` ja eram `public sealed record`.
- Padronizado para `public sealed record` (records de DTO em ports nao tem hierarquia).

#### 2. `ServiceCollectionExtensions.cs` (Infra.Async) — usings + FQN
- Removido `using EasyStock.Infra.Async;` (namespace ancestral do arquivo, redundante por regra de lookup do C#).
- Adicionados `using Microsoft.Extensions.Caching.Memory;` e `using Microsoft.Extensions.Logging;` para eliminar fully-qualified names dentro das factories `AddTransient<IEfiPixService>` e `AddTransient<IEfiBoletoService>`:
  - `Microsoft.Extensions.Caching.Memory.IMemoryCache` -> `IMemoryCache`
  - `Microsoft.Extensions.Logging.ILogger<T>` -> `ILogger<T>`
  - `System.Net.Http.HttpClient` -> `HttpClient` (System.Net.Http ja em ImplicitUsings).
- Mesmo padrao do fix em `MercadoPagoSignatureValidator` no commit anterior (fb4f08e).

### Padroes observados
- Factories `AddTransient<T>(sp => ...)` com DI manual sao foco de FQN residual: typar via `using` em vez de soletrar. Procurar mesmo padrao em demais `AddX<T>(sp => ...)` ao longo do projeto.
- `using` de namespace ancestral (`EasyStock.Infra.Async` em arquivo `EasyStock.Infra.Async.DependencyInjection.*`) nao quebra o build mas polui — varrer demais `DependencyInjection/` em rodadas futuras.
- Records de DTO/result em ports devem ser sempre `sealed` por convencao — proxima rodada checar `Application/Ports/Output/*` em massa.
