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

## 2026-05-07 (segunda rodada) — Admin + Web redesign/landing (Program.cs, Faturas, Tickets, DiagnosticoController)

### Contexto da rodada

Arquivos modificados na última hora incluíam componentes do Admin (novos page models de Faturas, Tickets, proxy endpoints mobile) e Web (redesign de landing, novos controllers de site). Muitas alterações de design system (cshtml, CSS, tokens) estavam em andamento — não commitadas. A limpeza aqui se aplica apenas aos arquivos `.cs` com mudanças só de minha autoria.

### Arquivos analisados nesta rodada

| Arquivo | Status |
|---|---|
| `EasyStock.Admin/Program.cs` | **Fix aplicado** |
| `Admin/Pages/Faturas/Dashboard.cshtml.cs` | **Fix aplicado** |
| `Admin/Pages/Faturas/Detail.cshtml.cs` | **Fix aplicado** (doc) |
| `Admin/Pages/Faturas/Emitir.cshtml.cs` | **Fix aplicado** |
| `Admin/Pages/Faturas/Index.cshtml.cs` | **Fix aplicado** (doc) |
| `Admin/Pages/Faturas/Emitir.cshtml.cs` | **Fix aplicado** |
| `Admin/Pages/Operacao/Index.cshtml.cs` | Limpo — já tinha XML doc |
| `Admin/Pages/Dispositivos/Index.cshtml.cs` | Limpo — já tinha XML doc |
| `Admin/Pages/Tickets/Detail.cshtml.cs` | **Fix aplicado** |
| `Admin/Services/AdminApiClient.cs` | Limpo — doc completo |
| `Web/Controllers/DiagnosticoController.cs` | **Fix aplicado** |
| `Web/Controllers/BaseController.cs` | Limpo — doc completo |
| `Web/Controllers/LojasController.cs` | Limpo |
| `Web/Controllers/UsuariosController.cs` | Limpo |
| `Web/Controllers/SiteController.cs` | Limpo — doc completo, sitemap/robots novos bem documentados |
| `Web/Models/ViewModels/Site/ContatoViewModel.cs` | Limpo — `MustBeTrueAttribute` com doc |
| `Web/Models/ViewModels/Site/LandingViewModel.cs` | Limpo — XML doc justifica classe vazia |
| `Web/Services/LeadsApiService.cs` | Limpo |
| `Web/Services/MarketingOptions.cs` | Limpo — doc completo por property |

### Fixes aplicados

#### 1. `EasyStock.Admin/Program.cs` — `using System.Text.Json;` + remoção de prefixos qualificados
- Adicionado `using System.Text.Json;` ao topo.
- Removidos ~30 prefixos `System.Text.Json.` espalhados pelo arquivo (JsonElement, JsonDocument, JsonSerializer, JsonValueKind).
- Removidos prefixos `System.IO.StreamReader` (System.IO já está nos global usings .NET 9).
- Motivo: o arquivo top-level não tinha o using, forçando qualificação completa em cada endpoint proxy.

#### 2. `Admin/Pages/Faturas/Dashboard.cshtml.cs` — doc + simplificação de GetAsync
- Adicionado XML doc na classe.
- Substituído `GetRawAsync` + extração manual de `"data"` por `GetAsync<JsonElement>` que já faz o unwrap. Reduz 2 linhas e elimina duplicação de lógica que existe em `AdminApiClient.UnwrapData`.

#### 3. `Admin/Pages/Faturas/Detail.cshtml.cs` e `Index.cshtml.cs` — XML doc
- Adicionado XML doc nas classes descrevendo responsabilidade e ações principais.

#### 4. `Admin/Pages/Faturas/Emitir.cshtml.cs` — doc + sync OnGet
- Adicionado XML doc na classe.
- Alterado `public Task OnGetAsync() => Task.CompletedTask;` para `public void OnGet() { }`. Um handler GET vazio não precisa de state machine de async; a versão sync evita alocação desnecessária.

#### 5. `Admin/Pages/Tickets/Detail.cshtml.cs` — doc + catch SessionExpiredException faltante
- Adicionado XML doc na classe.
- **Bug funcional corrigido**: `OnPostAssumirAsync`, `OnPostEncaminharAsync` e `OnPostBugFixAsync` não tinham `catch (SessionExpiredException) { throw; }` antes do `catch (Exception ex)`, ao contrário de todos os outros handlers do mesmo arquivo. Sem o rethrow, sessão expirada nessas ações gerava `SetErro("Falha ao assumir: Sessão expirada.")` em vez de redirecionar para login.

#### 6. `Web/Controllers/DiagnosticoController.cs` — doc + `[HttpGet]` explícito
- Adicionado XML doc na classe.
- Adicionado `[HttpGet]` em 6 actions que tinham apenas `[Route(...)]`: `Index`, `Json`, `ProxyEndpoints`, `ProxyHistorico`, `ProxyEnhancedLogs`, `ProxyExportarLogs`. Actions com somente `[Route]` aceitam qualquer método HTTP — para endpoints GET isso é inconsistente com o padrão dos métodos mais novos no mesmo controller.

### Padrões observados (aprendizado acumulado)

- Novos page models de Faturas/Tickets já nascem com boa cobertura funcional mas sem XML doc nas classes — adicionar é tarefa de rotina desta sessão.
- `GetRawAsync` + extração manual de `"data"` é um anti-padrão quando `GetAsync<T>` já encapsula isso. Trocar quando o caller não precisa de outras propriedades do envelope (ex: `meta`).
- Controllers com `[Route]` sem `[HttpGet]` são aceitos pelo framework mas permitem acesso via POST/PUT, o que não é intenção. Sempre adicionar `[HttpGet]` em endpoints read-only.
- Handlers de ação em Razor Pages Admin devem sempre ter `catch (SessionExpiredException) { throw; }` antes do `catch (Exception)` genérico. Sem isso, expiração de sessão aparece como erro de negócio no toast em vez de redirecionar para login.
