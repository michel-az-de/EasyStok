# Log de limpeza de código — limpa-codigo

Mantido pela tarefa agendada `limpa-codigo`. Cada entrada registra o que foi encontrado, o que foi corrigido e o que foi deixado intencionalmente.

---

## 2026-05-08 — Sweep dos commits de cleanup do dia anterior (4f9c259, fb4f08e)

Tarefa agendada `revisao-diaria-de-codigo` rodando em worktree `dev/festive-clarke-80b3f4`.

### Arquivos re-auditados

| Arquivo | Status |
|---|---|
| `EasyStock.Infra.Async/EfiPixService.cs` | Limpo (bloco vazio já removido em 4f9c259) |
| `EasyStock.Infra.Async/EfiBoletoService.cs` | Limpo — reusa mesmo `Efi:ClientId/Secret` que Pix |
| `EasyStock.Infra.Async/DependencyInjection/ServiceCollectionExtensions.cs` | Limpo — refactor `efiClientIdBoleto` reuso confirmado correto |
| `EasyStock.Infra.Async/Pagamentos/Webhooks/MercadoPagoSignatureValidator.cs` | Limpo |
| `EasyStock.Api/Controllers/AdminFaturasController.cs` | Limpo |
| `EasyStock.Api/BackgroundServices/BackgroundJobServiceCollectionExtensions.cs` | Limpo |
| `EasyStock.Application/Ports/Output/Persistence/IFaturaRepository.cs` | **Fix aplicado** (cref XML inválido) |
| `EasyStock.Application/Ports/Output/IEfiPixService.cs` | Limpo |
| `EasyStock.Infra.MongoDb/Repositories/MongoIdentityRepositories.cs` | **Fix aplicado** (comentário enganoso pós-ADR 0001) |

### Fixes aplicados

#### 1. `IFaturaRepository.cs` — `<see cref="EmpresaId"/>` inválido
- `EmpresaId` é uma propriedade da entidade `Fatura`, não um símbolo top-level. O cref XML não resolve para nada.
- Trocado por `<c>EmpresaId</c>` (texto monoespaçado).
- Sem warning de build (`EasyStock.Application` não tem `<GenerateDocumentationFile>`), mas a ref ainda assim é incorreta — corrigida por consistência.

#### 2. `MongoIdentityRepositories.cs` — comentário enganoso sobre ativação de Mongo
- O commit fb4f08e corrigiu um comentário stub→funcional, porém afirmou "lê e grava em Mongo se o backend Mongo for ativado".
- Conflita com ADR 0001 (`docs/adr/0001-mongo-discarded.md`, 2026-05-01): runtime lança `NotSupportedException` quando `Database:Provider=MongoDB` — não há como "ativar" o backend.
- Reescrito como: "Mongo backend descartado (ADR 0001) — runtime lança NotSupportedException. Código mantido fisicamente para histórico; lê/grava normalmente caso alguém revigore o branch Mongo no futuro."

### Build

`dotnet build EasyStok.sln` — 0 erros, 17 avisos (todos pré-existentes: deprecação `Window.SetStatusBarColor` MAUI, XAML XC0025/XC0045 bindings, XA0141 Android 16 page-size, xUnit1031 em `EstoqueConcurrencyTests.cs`). Nada introduzido pela limpeza.

### Padrões observados

- Stale comments seguem sendo o principal vetor de defeito após cleanup parcial — corrigir um comentário sobre stub→funcional sem checar a ADR levou a um segundo comentário enganoso.
- ADRs (`docs/adr/`) precisam ser consultadas ao reescrever comentários sobre infra alternativa (Mongo, gateways stub, etc).
- `<see cref="..."/>` em XML doc só gera warning quando o projeto tem `<GenerateDocumentationFile>true</GenerateDocumentationFile>` — `EasyStock.Application` não tem, então crefs inválidos passam silenciosamente.

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
