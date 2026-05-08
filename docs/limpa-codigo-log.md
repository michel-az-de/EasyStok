# Log de limpeza de código — limpa-codigo

Mantido pela tarefa agendada `limpa-codigo`. Cada entrada registra o que foi encontrado, o que foi corrigido e o que foi deixado intencionalmente.

---

## 2026-05-07 — Cleanup fb4f08e: polish billing F12 + Mongo (segunda passagem)

### Arquivos analisados (4 arquivos .cs do commit fb4f08e)

| Arquivo | Status |
|---|---|
| `IEfiPixService.cs` | **Fix aplicado** — XML doc adicionado em `CriarCobrancaAsync` |
| `ServiceCollectionExtensions.cs` (Infra.Async) | **Fix aplicado** — variável redundante removida |
| `MercadoPagoSignatureValidator.cs` | **Fix aplicado** — referências fully-qualified removidas |
| `MongoIdentityRepositories.cs` | **Fix aplicado** — comentário stale corrigido |

### Fixes aplicados

#### 1. `IEfiPixService.cs` — XML doc completado
- `CriarCobrancaAsync` não tinha `<summary>` enquanto `ConsultarCobrancaAsync` e `EstornarAsync` já tinham.
- Doc adicionado descrevendo o endpoint `POST /v2/cob/{txid}` e o retorno (QR code + copia-e-cola).

#### 2. `ServiceCollectionExtensions.cs` — variável redundante removida
- `efiClientIdBoleto` relia `configuration["Efi:ClientId"]` sendo que `efiClientId` já havia lido o mesmo valor.
- O bloco Boleto passou a reutilizar `efiClientId` (lido na linha 63).

#### 3. `MercadoPagoSignatureValidator.cs` — usings corrigidos
- Faltava `using System.Text.Json`; o código usava `System.Text.Json.JsonDocument` e `System.Text.Json.JsonValueKind` fully-qualified.
- Padrão já adotado em `EfiPixService.cs`, `EfiPixWebhookProcessor.cs` etc.

#### 4. `MongoIdentityRepositories.cs` — comentário stale corrigido
- `FornecedorRepository` tinha comentário "stub / retorna lista vazia / ignora write" (Onda P4 inicial).
- Corrigido para descrever o estado real: código lê e grava via `EnqueueInsert`/`Find`.

### Verificações desta rodada (nenhuma alteração necessária)

| Arquivo | Resultado |
|---|---|
| `MetricasFinanceirasUseCase.cs` | Limpo após fix `191f685` — comentários de multi-tenant e janela 365d são intencionais |
| `AssinaturaEmpresaRepository.cs` (Postgre) | Limpo — `IgnoreQueryFilters()` intencional em métricas; JOIN explícito documentado |
| `IAssinaturaEmpresaRepository.cs` | Limpo — fixes do round anterior mantidos |
| `ResetTokenRepository` (em MongoIdentityRepositories) | Referência fully-qualified `TokenHashHelper` deixada intencional (uso único, evita poluir usings) |

### Padrões observados

- Segunda passagem de cleanup ocorre quando a primeira rodada marca arquivo como "Limpo" mas usando deixa escorrer algum detalhe (falta de XML doc em método novo, variável renomeada mas não atualizada nos pares).
- Arquivos com múltiplas classes (ex: MongoIdentityRepositories, 738 linhas) acumulam mais facilmente comentários stale em classes menos usadas — monitorar com atenção.

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
