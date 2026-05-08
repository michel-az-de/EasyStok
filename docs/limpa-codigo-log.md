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

## 2026-05-08 — Pós-cleanup billing (auditoria autônoma agendada)

Rodada de auditoria sobre os commits `4f9c259` e `fb4f08e` (cleanup F10–F14 + F12/Mongo). Build OK (0 erros, 17 avisos pré-existentes em MAUI/test).

### Fixes aplicados

#### 1. `IEfiPixService.cs` — XML doc com método HTTP errado
- Antes: `<summary>Emite uma cobranca Pix imediata via <c>POST /v2/cob/{txid}</c>`.
- Depois: `<summary>... via <c>PUT /v2/cob/{txid}</c> (txid definido pelo cliente, idempotente)`.
- Motivo: a implementação em `EfiPixService.EnviarCobrancaAsync` usa `HttpMethod.Put` (consistente com a API Efi para criação idempotente de cobrança com txid pré-definido). XML doc adicionado em `fb4f08e` referenciava POST por engano.

#### 2. `FaturaTemplate.cs` — overload de método nunca chamada removida
- Removida: `private static void ComposeEnderecoLinhas(QuestPDF.Infrastructure.IContainer _, Endereco? endereco) { /* sobrecarga indireta — ver abaixo */ }`
- Motivo: ambos os call sites (`linhas 131` e `148`) passam `ColumnDescriptor` (variável `col` do lambda do QuestPDF), nunca `IContainer`. A overload com `IContainer` é genuinamente código morto — o comentário "sobrecarga indireta — ver abaixo" descrevia uma intenção que não se materializou em uso.
- Build do projeto Infra.Async pós-remoção: 0 avisos, 0 erros.

### Itens deixados intencionalmente

- **Avisos `XC0045` em LoginPage/LojaPickerPage/TenantPickerPage do MAUI** (LoginCommand/LogoutCommand não encontrados nos ViewModels): defeito real de binding XAML, mas fora de escopo do cleanup billing. PWA é fonte da verdade do operador; investigar no MAUI requer contexto separado.
- **Avisos `xUnit1031` em `EstoqueConcurrencyTests`**: blocking task ops em testes de concorrência podem ser intencionais (testar race conditions com sleep/Task.Wait determinístico). Não tocar sem alinhar com Felipe.
- **Avisos `CA1422`/`CS0618` em `MainActivity.cs` e `ThemeService.cs`**: APIs Android obsoletas em Android 35+; refactor de plataforma fora do escopo de limpeza.

### Padrões adicionais observados

- XML docs adicionados na rodada anterior podem ter erros sutis (verbo HTTP, endpoint) — vale conferir a implementação ao adicionar doc novo, não só copiar de outros métodos.
- Overloads "fantasma" de helpers privados (criadas durante experimentação e nunca chamadas) não são pegas pelo compilador C# como "unused private" quando há outra overload com mesmo nome — vale `Find Usages` em PDF templates / helpers privados.
