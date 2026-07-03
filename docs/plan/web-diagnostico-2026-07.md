# Diagnóstico EasyStock.Web — 2026-07-02

Épico: [#795](https://github.com/michel-az-de/EasyStok/issues/795). Issues-filhas: #796–#812 + comentário na #714.

## Metodologia

Varredura em 4 frentes paralelas sobre o projeto inteiro (229 .cs / 14.488 linhas, 129 views / 25.961 linhas, 41 JS / 6.212 linhas), com regra vinculante de que **nenhum achado entra sem evidência medida** (arquivo:linha + `git grep`/leitura do código; suspeitas de código morto confirmadas com zero referências incluindo `asp-action`, `Url.Action`, URLs literais e concatenadas em JS, partials por nome e consumidores externos Admin/CI/capacitor). Os achados P1 foram re-verificados de forma independente antes de virar issue. Duplicatas com as ~150 issues abertas foram eliminadas (fuso → #553/#556, `@@` em forms → #714, FormatHelper Web×Admin → #782, CSP Admin → #355, bundling → #778).

## Estado geral (medido)

O projeto está **melhor do que o esperado** para uma varredura desta profundidade:

- Segurança: antiforgery automático global, `[Authorize]` global via BaseController, cookies de sessão/`_rt` com HttpOnly+Secure+SameSite.Strict, open redirect tratado com `Url.IsLocalUrl`, sinks de XSS auditados todos encodados (`JsonSerializer.Serialize` / `HtmlEncoder`), zero segredos hardcoded. **Nenhum XSS/CSRF/open-redirect explorável encontrado.**
- Higiene: IDE0005 como erro de build, zero código comentado em bloco, zero `#pragma`/supressões, 1 TODO real.
- Padrões corretos já existem no repo para quase todas as classes de bug achadas (BrazilTime, esFetch, pedidos-cockpit com rollback, TryParse) — os achados são majoritariamente pontos que **não migraram** para o padrão.

## Achados por severidade

### P1 — corrigir primeiro

| Issue | Achado | Evidência-chave |
|---|---|---|
| #796 | `TokenRefreshHandler._isRefreshing` é campo de instância num handler pooled pelo IHttpClientFactory → compartilhado entre todos os usuários; race causa logout indevido e 401 espúrio; retry mantém empresaId stale | `TokenRefreshHandler.cs:15,51,56,75` |
| #797 | `EnumStringOrIntConverter` mapeia enum "StatusPedidoCompra" que não existe; o real é `StatusPedidoFornecedor` (1-based, nomes diferentes) → badge de pedido de fornecedor exibe "0" | `EnumStringOrIntConverter.cs:25-30` vs `Domain/Enums/StatusPedidoFornecedor.cs` |
| #798 | 3 botões do lightbox sem `type="button"` dentro do form de produto → navegar foto na revisão submete o form (criação) ou abre confirmação (edição) | `Produtos/Form.cshtml:1064,1067,1074` |
| #714 (comentário) | 12 views com `@@submit*` em `<form>` que o FormTagHelper descarta; 8 com comportamento quebrado (confirmação de cancelamento pula direto, relatório ignora período, validação do caixa morta) | ver comentário de 2026-07-02 na #714 |

### P2 — comportamento incorreto / hardening relevante

- #799 — ApiClient: todo 429 vira "Cota de IA esgotada" (mascara rate-limit de login, incidente #747).
- #800 — Registrar: validação de e-mail/CNPJ chama `/api/empresas/*` que não existe no host do Web → nunca funciona em produção (classe #550/#551).
- #801 — `x-init="init()"` + auto-invoke do Alpine → init 2x: cheatsheet morto, scanner/câmera duplicados, fetches 2x.
- #804 — Timestamps UTC exibidos sem conversão BRT: `FormatHelper.AsDate/AsDateTime(DateTimeOffset)` via `LocalDateTime` + 4 pontos com `.ToString()` cru (classe #553).
- #805 — Filtro "vencendo" do Estoque diverge do predicado canônico da Api + `DateTime.Today` UTC desliza a janela 21h–23:59 BRT.
- #806 — Chrome global: topbar marca-lida sem `res.ok` (mutação otimista falsa), cache de categorias do sidebar cross-loja sem invalidação, `api.js`×`es-fetch.js` com contratos divergentes.
- #807 — CSV Entradas (invariant) × Saídas (pt-BR) com culture divergente; Excel pt-BR corrompe o de Entradas.
- #802 — `/auth/impersonate`: anônimo + sem antiforgery + lê claims de JWT sem validar assinatura antes de emitir cookie de sessão (mitigado pela validação da Api no 1º request de dados).
- #803 — Ausência total de CSP (espelho Web da #355) — rede de segurança faltante para dezenas de scripts inline.

### P3 — dívida técnica

- #808 — Lote de 8 itens pequenos: `Guid.Parse` sem TryParse (500→400), `_rt` órfão em login abortado, dispose do `GetStreamAsync`, binder decimal vazio→0 silencioso, `_rt` Secure divergente no SessionRestore, SSE de erro em texto cru, "hoje BR" com offset hardcoded, cookie de auth sem SameSite/Secure explícitos + fixation.
- #809 — Código morto medido: `dashboard-charts.js` (~470 linhas) + `theme-toggle.js`, 12 actions (5 do Dashboard, restaurar/upload-foto de Produtos, `Lotes.Imprimir`, etc.), 8 partials órfãs, 4 TagHelpers com 0 usos, 4 models. Ressalva: `Downloads.Manifest` exige checagem de logs (APK antigo em campo) antes de deletar.
- #810 — Redundância: `GetEmpresaId()` ×25 + variante divergente, `EmpresaErr<T>()` ×6, `Loja`×`LojaApi` pro mesmo endpoint, formatação manual em 15 pontos apesar do `format.js`.
- #811 — Views: Criar de CAP×CAR ~85% idênticas, modais Clientes×Fornecedores duplicados, x-data de ~900 (Pedidos) e ~800 (Produtos/Form) linhas inline.
- #812 — Reordenação otimista sem rollback/single-flight (fotos do produto, cardápio) + `masks.js` sem re-dispatch (classe #497).

## Plano de execução (4 ondas, todas fatiáveis em commits build-verdes)

**Onda 1 — P1 (estimativa: 3-4 sessões curtas).** #796 (única com risco de design — single-flight por sessão; escrever teste de concorrência), #797 e #798 são fatias de <50 LoC cada; a varredura da #714 é mecânica (12 views, mesmo diff) + guard de arquitetura.

**Onda 2 — P2 de comportamento (4-5 sessões).** Ordem sugerida: #799 e #807 (pequenas), #801 (mecânica), #804+#805 juntas (mesmo tema fuso, mesmo helper), #800 (novo endpoint proxy, pré-auth → cuidado com rate-limit), #806 por último (3 sub-itens independentes).

**Onda 3 — segurança (2-3 sessões).** #802 toca Admin+Web no mesmo commit (R8); #803 começa report-only e só vira enforce depois de calibrar em produção — depende de decisão sobre nonce vs `unsafe-inline` (registrar na issue, PS5).

**Onda 4 — dívida (oportunista, sem prazo).** #809 primeiro (deletar reduz a superfície das demais), depois #808, #810, #812; #811 por último (maior risco de regressão visual, exige smoke manual).

Regras transversais: cada onda referencia `refs #795`; guards de arquitetura novos (button-sem-type, `@@submit` em views, init duplicado) entram junto com o fix da classe correspondente para o problema não voltar; nenhum item da onda 4 mistura com fixes de comportamento no mesmo commit.

## Fora de escopo (registrado, não esquecido)

- Robustez multi-tenant real (empresaId do query vs JWT) é responsabilidade da Api — coberta pelo code review da Api (memória: 22 achados, Grupo B pendente).
- `wwwroot/etiqueta/**` é cópia de build da Api — qualquer achado ali pertence à fonte.
- TODO único (`SiteController.cs:86`, XFF no rate-limit de leads) → já coberto pela triagem #781.
