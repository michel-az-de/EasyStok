# Log de limpeza de código — limpa-codigo

Mantido pela tarefa agendada `limpa-codigo`. Cada entrada registra o que foi encontrado, o que foi corrigido e o que foi deixado intencionalmente.

---

## 2026-05-11 — Commit 7cd88e8: fix(web) a11y WCAG 2.1 AA

### Arquivos analisados (28 arquivos do commit 7cd88e8)

Commit de contraste/acessibilidade. Mudanças foram: `text-emerald-700 → text-emerald-800`, `text-amber-700 → text-amber-800` em pills/badges, `aria-label` em botões ícone, `for`+`id` em form labels, dark mode focus ring, `_ConfirmModal` width responsiva.

| Arquivo | Status |
|---|---|
| `Fornecedores/Detail.cshtml` | **Fix aplicado** — variável alias removida + classe CSS morta removida |
| `Produtos/Detail.cshtml` | **Fix aplicado** — `hover:text-emerald-800` corrigido para `hover:text-emerald-900` |
| Demais 26 arquivos | Limpos — mudanças de contraste são corretas, sem código morto |

### Fixes aplicados

#### 1. `Fornecedores/Detail.cshtml` — variável alias e classe CSS morta removidas

- `var visiveis = Model.Alteracoes;` era alias puro sem propósito. Substituído por `Model.Alteracoes` direto no loop `for`.
- `var classeOculta = idx >= 10 ? "alt-extra" : "";` adicionava classe `alt-extra` que não existe em nenhum arquivo CSS (nem `components.css`, nem `app.css`, nem `tokens.css`). A visibilidade dos itens extras é controlada inteiramente por `x-show="expanded"` do Alpine.js — a classe era 100% código morto.
- Linha resultante fica `<li class="py-2 text-sm"` (sem classe fantasma e sem espaço trailing).

#### 2. `Produtos/Detail.cshtml` — hover state sem efeito corrigido

- O fix de contraste `text-emerald-700 → text-emerald-800` foi feito corretamente no estado normal, mas a classe hover não foi ajustada: ficou `hover:text-emerald-800`, igual ao estado padrão.
- Hover de cor não tinha efeito visual (o `hover:underline` ainda funcionava, mas o escurecimento foi perdido).
- Corrigido para `hover:text-emerald-900` — restaura feedback visual de hover.

### Verificações desta rodada (nenhuma alteração necessária)

| Arquivo / Elemento | Resultado |
|---|---|
| `console.*` em Diagnóstico, Entradas, Produtos/Form, Pedidos, Saídas, Topbar | Intencional — logging de diagnóstico e error handling com toast |
| Sem código comentado (`@* old/remov/... *@`) | Nenhum encontrado |
| Sem TODO/FIXME nos arquivos | Nenhum encontrado |
| `_ConfirmModal.cshtml` | Limpo — componente Alpine bem estruturado |
| `components.css` — focus ring dark mode | Correto — comentário explicativo presente |
| Back links com SVG em Lotes/Detail, Fornecedores/Detail, Movimentacoes, PedidosAbertos | Corretos — `aria-label` no `<a>` e `aria-hidden="true"` no SVG |
| `Clientes/Detail.cshtml` — `@d.EmitidoEm.Value.ToLocalTime():dd/MM/yyyy` | Correto — sintaxe Razor para `IFormattable`, equivalente a `.ToString("dd/MM/yyyy")` |
| Produtos/Detail back link (SVG sem `aria-hidden`) | Mantido — `<a>` tem texto visível "Produtos", link é acessível sem o atributo |

### Arquivos fora do commit com contraste pendente (não corrigidos nesta rodada)

A busca por `text-emerald-700` e `text-amber-700` revelou ocorrências em arquivos NÃO incluídos no commit 7cd88e8 que têm o mesmo padrão de contraste insuficiente:

| Arquivo | Classes encontradas | Contexto |
|---|---|---|
| `InteligenciaLojas/Index.cshtml` | `bg-amber-100 text-amber-700`, `bg-emerald-100 text-emerald-700` | Pills de insight/saúde |
| `InteligenciaLojas/Detalhe.cshtml` | `bg-amber-100 text-amber-700`, `bg-emerald-100 text-emerald-700` | Pills de recomendação |
| `Shared/_BottomNav.cshtml` | `bg-emerald-50 text-emerald-700`, `bg-amber-50 text-amber-700` | Quick-action items |
| `Shared/_Topbar.cshtml` | `bg-emerald-50 text-emerald-700`, `bg-amber-100 text-amber-700` | Badge de confirmação e tags de resultado de busca |
| `Estoque/Index.cshtml` | `bg-emerald-50 text-emerald-700`, `bg-amber-50 text-amber-700` | Tabs de natureza |
| `Produtos/Form.cshtml` | `bg-emerald-50 text-emerald-700` | Status pill de item de fila |

Ação sugerida: estender o fix de contraste para esses arquivos em uma próxima tarefa dedicada.

### Padrões observados

- Fix de contraste sistemático via replace_all pode introduzir hover states sem efeito (ex: `hover:text-emerald-800` após o base ser promovido para 800). Ao fazer replace de cores em lote, sempre checar pares `text-X / hover:text-X`.
- Variáveis alias em Razor (ex: `var visiveis = Model.Foo;`) surgem quando o dev começa a filtrar a lista mas abandona. Monitorar em código de paginação/collapso.
- Classes CSS "placeholder" (ex: `alt-extra`) podem ser criadas com intenção de estilizar depois mas esquecidas quando o Alpine.js resolve a funcionalidade — sempre confirmar existência no CSS antes de usar em Razor.

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

## 2026-05-08 — Cleanup Ports/Output (Application) + Adapters internacionais (Infra.Async)

### Achados (revisao diaria automatica)

Sem commits nas ultimas 30min. Vistoria nos arquivos adjacentes ao trabalho F10-F14 (commits fb4f08e + 4f9c259) revelou padroes residuais:

| Arquivo | Fix |
|---|---|
| `IFaturaNumeradorService.cs` | usings BCL redundantes removidos (System, System.Threading, System.Threading.Tasks) |
| `IUnitOfWork.cs` | using System.Threading.Tasks removido + namespace file-scoped |
| `IDbTransactionScope.cs` | usings BCL redundantes removidos + namespace file-scoped |
| `IGeradorDescricaoAnuncio.cs` | using System.Threading.Tasks removido + namespace file-scoped |
| `IAsyncInfrastructure.cs` | using System.Text.Json nao utilizado removido |
| `StripeGatewayAdapter.cs` | adiciona using System.Text.Json + remove fully-qualified JsonSerializer |
| `MercadoPagoGatewayAdapter.cs` | mesma padronizacao System.Text.Json |

### Validacao

- `dotnet build EasyStok.sln` — 0 erros, 0 warnings.
- Mesmo padrao do cleanup recente (consistencia com fb4f08e que fez isso em MercadoPagoSignatureValidator).

### Padroes nao tocados (intencionais)

- `EfiPixWebhookProcessor.cs` linha 174 (`assinatura.DataFim = baseDate.AddDays(30)`) hardcoda 30 dias — design decision para Pix mensal; refator para usar Plano.CicloMeses fica fora do escopo de cleanup, vai pro tech-debt.
- `AutoTicketFalhaPagamento.cs` parametro `empresaId` so usado em log fallback — assinatura do port precisa do parametro mesmo quando faturaId nao resolve; nao e dead code.
- `ConsoleEmailService` em `ServiceCollectionExtensions.cs` (Infra.Async, fora do namespace de DI) — fallback dev intencional, nao remover.
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
