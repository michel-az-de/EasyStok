# Log de limpeza de código — limpa-codigo

Mantido pela tarefa agendada `limpa-codigo`. Cada entrada registra o que foi encontrado, o que foi corrigido e o que foi deixado intencionalmente.

---

## 2026-05-11 — Commit 5bc4648: fix(pedidos) reset no reabrir + cadastrar produto sempre visível

### Arquivos analisados (2 arquivos do commit 5bc4648)

| Arquivo | Status |
|---|---|
| `EasyStock.Web/Views/Pedidos/Index.cshtml` | **Fix aplicado** — guarda morta removida + comentário corrigido |
| `EasyStock.Web/wwwroot/css/app.css` | **Fix aplicado** — 2 blocos de CSS morto removidos |

### Fixes aplicados

#### 1. `Index.cshtml` — guarda morta em `comboboxNavega` removida

- `if (max < 0) return;` nunca podia ser verdadeiro: `clientesFiltrados.length` é sempre `>= 0`.
- Comentário `// +1 = "cadastrar novo"` era enganoso — o `+1` nunca estava no código. Corrigido para explicar que `max === length` = índice da opção "Cadastrar novo" (última da lista, 0-indexado).

#### 2. `app.css` — `.ped-combobox__clear` e `.ped-combobox__empty` removidos

- `.ped-combobox__clear` (+ `:hover`): botão "X" para limpar o combobox de cliente. A UI usa um `<button>` com classes Tailwind inline para essa função; a classe CSS nunca foi aplicada a nenhum elemento.
- `.ped-combobox__empty`: estado "sem resultados" do combobox. O template atual não renderiza esse elemento — lista desaparece ou mostra apenas "Cadastrar novo".
- Confirmado ausência em `EasyStock.Web/**`, `EasyStock.Api/wwwroot/pwa/**` e `EasyStok.Mobile/Resources/Raw/pwa/**`.

### Verificações desta rodada (nenhuma alteração necessária)

| Arquivo / Elemento | Resultado |
|---|---|
| `.ped-tab`, `.ped-tabs` (CSS) | Usados em `Views/Caixa/Index.cshtml` — não remover |
| `console.log/group/table` no submit | Intencional — logging de diagnóstico do operador |
| `tempoRelativo` definido 2× (C# + JS) | Funções distintas: C# = server-side Razor, JS = Alpine.js client-side |
| `_preflight` duplica lógica de `clienteValido` | Intencional — `podeEnviar` controla estado do botão, `_preflight` gera mensagem detalhada |
| CSS novo: `.ped-prod-dropdown__create` sticky | Correto — resolve bug de visibilidade do botão "+ Cadastrar como novo produto" |
| CSS novo: `.ped-prod-input--pendente` + `.ped-prod-pendente-hint` | Correto — feedback visual de item não consolidado |

### Padrões observados

- Guardar `length` de array em `max` sem adicionarmos o +1 que o comentário mencionava é um padrão desleixado que sobrevive em loops de keyboard-nav — varrer outros comboboxes se forem criados no projeto.
- CSS utility classes (clear button, empty state) são pré-criadas junto com o componente mas removidas do template quando o design muda, ficando como dead CSS. Monitorar esse padrão em novos componentes Alpine.

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

---

## 2026-05-07 — Auditoria #2 (rodada agendada apos limpeza F10-F14, commit 4f9c259)

### Bug de seguranca encontrado e corrigido

**`MercadoPagoSignatureValidator.cs` — replay protection ausente.** O validador parseava o campo `ts` do header `x-signature` mas usava apenas dentro do `toSign` (corpo do HMAC). **Sem comparacao com o tempo atual**, um atacante que capturasse uma chamada de webhook MP podia replayar indefinidamente. O validador Stripe ja tinha `±5min` validado — paridade quebrada.

**Fix**: janela `±5min` no MP. MP envia `ts` em **unix milissegundos** (Stripe usa segundos) — heuristica `< 10_000_000_000L → segundos` aceita os dois formatos (sandbox MP antigo enviava em seg). `ts` invalido (nao parseavel) tambem recusa.

**Cobertura de teste**: novo `EasyStock.Api.UnitTests/Pagamentos/MercadoPagoSignatureValidatorTests.cs` com 7 cenarios — sem header, hmac correto, hmac incorreto, ts atual em ms, ts atual em s, ts fora-da-janela, allow-unsigned, payload invalido. `dotnet test` passou em 29/29.

### Outros achados (sem patch — deliberado)

| Item | Decisao |
|---|---|
| `AutoTicketFalhaPagamento`: param `empresaId` nao e validado contra `fatura.EmpresaId` | OK — ticket usa `fatura.EmpresaId` (ground truth do agregado), entao mismatch nao causa vazamento. Param fica defensivo (poderia logar warning, mas nao e bug). |
| `MediaDiasAtrasoVencidasAsync` materializa `DateTime` em memoria pra calcular media | OK — comentario explica que EF nao traduz `DateDiff` cross-provider. Para ~1000s de vencidas e aceitavel. |
| Webhook MP/Stripe registrados sem `IGatewayWebhookProcessor` correspondente | OK — adapters sao stubs documentados. Quando F12-real for feita, o processor entra junto. |

### Padrao novo (registrar)

- **Webhook signature validators precisam de paridade.** Ao adicionar um novo validator (Stripe/MP/Pix/etc.), conferir se ele tem **todas** as protecoes que os outros ja tem: replay window, fixed-time hash compare, allow-unsigned guard, secret obrigatorio, header obrigatorio. Quebra de paridade = vulnerabilidade silenciosa.
- MP usa `ts` em **milissegundos** unix epoch; Stripe usa **segundos**. Diferenca facil de errar — verificar a doc do gateway antes.
- Quando criar adapter stub, criar **teste correspondente** ao validator/processor mesmo que basico — garante que cobertura nao fica desigual entre gateways.
