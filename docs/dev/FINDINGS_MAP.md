# FINDINGS_MAP — Saneamento QA app cliente + Admin (2026-06-30)

Entregável #1 da missão `/incident-response`. Cada achado da QA black-box foi tratado como
**hipótese** (R1: inferido da UI, não do código) e confirmado/refutado na base de código com
evidência `arquivo:linha`, por 3 agentes de investigação read-only.

**Meta-achado (R10/PS1/PS7):** a QA por trás desta missão auditou o **deploy da VM**, que atrasa
~2h de build (#751) e não reflete o repo. A maior parte do Bloco A (consistência de dados) já
estava corrigida ou em andamento em master. Padrão "black-box false negative por deploy lag".

Legenda de veredito: **JÁ-CORRIGIDO** (fix em master, falta deploy) · **REFUTADO** (não é bug /
by-design) · **PARCIAL** (em andamento, issue existe) · **ABERTO** (confirmado, sem cobertura).

## App cliente (app.20.230.185.203.sslip.io)

| ID | Sintoma | Veredito | Evidência arquivo:linha | Issue/PR alvo |
|----|---------|----------|-------------------------|---------------|
| AC-1 | Reposição contraditória (Dashboard 6 × Análises 7 × "tudo abastecido") | JÁ-CORRIGIDO | ADR-0039; `EasyStock.Application/UseCases/Analytics/Reposicao/ObterReposicaoUseCase.cs`; `AnalisadorReposicao` (fatias 0-5 commitadas) | #748 |
| AC-2 | Divergência de receita em 3 telas | PARCIAL | `EasyStock.Infra.Postgre/Repositories/RevenueMetricsQueries.cs:36-38` (fonte única criada, F1); telas ainda por migrar | #754 (F2-F4) |
| AC-3 | Divergência de caixa Dashboard × /caixa (Δ R$60) | JÁ-CORRIGIDO | commit `8099284f` "saldo do caixa unificado entre /caixa e Dashboard/Admin" | — |
| AC-4 | Venda conta no total mas some de "Movimentos do dia" | PARCIAL→ESTA SESSÃO | backend `CaixaSaldoCalculator.GetLinhasExtrasAsync` (não-commitado); falta fiar `LinhasExtras` no BFF Web (`EasyStock.Web/Models/Api/Caixa.cs`) + view (`Views/Caixa/Index.cshtml:195-240`) | #NNN (esta sessão) |
| AC-5 | Limiares default contraditórios (99/5, "crítico<mínimo") | REFUTADO + JÁ-CORRIGIDO | default real é 5/2 e o resolver normaliza (refutado por #748); validação crítico<mínimo no commit `38da6821` | #748 |
| AC-6 | Deep-link/refresh em `/contas-a-pagar/{id}` cai na listagem | NÃO-INVESTIGADO nesta rodada | — (rota Web de detalhe; confirmar em sessão de routing) | a triar |
| AC-7 | Anúncios IA sempre falha (optimistic lock, HTTP 200 com corpo de erro) | PARCIAL | `EasyStock.Api/Controllers/IaAnuncioController.cs:34-67` (SSE emite 200 antes do erro); `AnuncioIa` sem RowVersion; erro honesto já feito (`8fa5ebc1`); causa-raiz aberta | abrir issue |
| AC-8 | Venda avulsa exige identificação | PARCIAL | backend já aceita anônimo `FinalizarVendaBalcaoUseCase.cs:173-187` (`ClienteNomeAdHoc`); fricção só no form Web | abrir issue |
| AC-9 | "Criar lista" a partir de Análises não pré-populada | JÁ-CORRIGIDO | `EasyStock.Web/Controllers/ListasComprasController.cs:36-47` popula via `SugestaoReposicaoListaAsync` | #748 |
| AC-10 | Badge "Lotes e validade (2)" leva a "Nenhum lote" | JÁ-CORRIGIDO | commit `596996ab` (badge aponta p/ `/estoque?status=vencido`) | — |
| AC-11 | Saídas com granularidade explodida (1 venda → N linhas) | ABERTO | `EasyStock.Web/Views/Saidas/Historico.cshtml:122` lista `MovimentacaoEstoque` item-a-item; tem `VendaId`, não agrupa | abrir issue |
| Higiene | Produtos XSS-escapado e item R$142.857.142,84 poluindo agregados | ABERTO | `EasyStock.Domain/Entities/Empresa.cs:14` `IsSeedData` é `[NotMapped]` (não persiste) → não filtra agregados | abrir issue (p3) |

## Admin (admin.20.230.185.203.sslip.io)

| ID | Sintoma | Veredito | Evidência arquivo:linha | Issue/PR alvo |
|----|---------|----------|-------------------------|---------------|
| AD-1 | MRR é preço de catálogo, rotulado "Receita recorrente" e "Estimativa" | PARCIAL | fonte `RevenueMetricsQueries.cs:36-38` distingue contratado/faturado/recebido; view ainda "RECEITA MENSAL RECORRENTE"; `AdminDashboardQueries.cs:20-22` diverge | #754 |
| AD-2 | Cobrança recorrente não gera faturas de assinatura | PARCIAL | `EasyStock.Api/BackgroundServices/CobrancaAssinaturaJob.cs:120-184` existe e emite por ciclo; falta flag `EnableRecorrenciaFaturamentoJob` | #754/#700 |
| AD-3 | Health-score de tenant (churn) "já existe painel, só plugar" | REFUTADO | 0 matches de painel de health/`StoreIntelligence` na Operação — painel **não existe** | (nova feature, não bug) |
| AD-4 | Storefronts falso-vazio sob falha de listagem | PARCIAL | `EmptyStateTagHelper.cs` com `variant="erro"` existe mas não usado; `Pages/Storefronts/Index.cshtml.cs:42-47` e `Tickets/Index.cshtml.cs:62-66` caem em vazio; Tenants já faz erro inline | #730 (Onda 0) |
| AD-5 | `/Notificacoes` (rota-pai) 404 | ABERTO | `Pages/_Layout.cshtml:208` aponta p/ `/Notificacoes/Templates`; sem Index na raiz | #730 (nav) |
| AD-6 | Página 404 perde a sidebar | REFUTADO | `Pages/Error.cshtml:4` `Layout=null` **proposital** ("standalone à prova de falha") | — |
| AD-7 | Detalhe de Ticket robustez de hidratação | REFUTADO (já robusto) | `Pages/Tickets/Detail.cshtml.cs:88-101` hidrata via `OnGetAsync` (deep-link ok); falta só teste de proteção | #730 (teste) |
| AD-8 | Grafia da marca inconsistente (EasyStok × EasyStock) | ABERTO | ~34 "EasyStok" vs ~362 "EasyStock"; `_Layout.cshtml:117` logo "Stok" vs `Error.cshtml:11` title "Stock"; não centralizado | abrir issue (decisão de grafia) |
| AD-9 | Rótulo MRR "Recorrente" × "Estimativa" | PARCIAL | mesmo que AD-1 | #754 |
| AD-10 | Terminologia mista (Tenant/Cliente/Loja etc.) | ABERTO (glossário) | disperso; sem glossário central | #730 (copy) |
| AD-11 | Pluralização genérica ("storefront(s)") | ABERTO | disperso nas views Admin | #730 (copy) |
| AD-12 | Selo "OPS" sem rótulo/tooltip | ABERTO | `_Layout.cshtml:121` `<span ...>ops</span>` sem title/aria-label | #730 |
| AD-13 | Dessincronia header↔nav↔conteúdo | REFUTADO | `_Layout.cshtml:156-219` `is-active` é SSR (não client-side) | — |
| AD-14 | Badges da sidebar intermitentes | REFUTADO | badges hidratados via `window.__initialDash` + `fetchDashBadges()` a cada 60s (pausa em aba oculta = by-design) | — |
| AD-15 | Baixa descoberta de "Matriz SLA"/"Canais de notificação" | ABERTO | `Configuracoes/Index.cshtml:115-116` só em Links rápidos; fora do menu principal | #730 |
| AD-16 | Cupons sem expiração (desconto perpétuo) | ABERTO | `Domain/Entities/Cupom.cs:34-35` permite `ValidoAte==null && LimiteUsos==null`; sem guard na UI | abrir issue |
| AD-17 | Tickets com SLA violado; dashboard × badge divergem | PARCIAL | `AdminTicket.cs:29-30` booleanos persistidos (fonte única); diverge só semântica "violado em finalizado" (`HelpdeskDashboardService.cs:69-75` exclui finalizados; `Tickets/Index.cshtml:139` mostra badge) | #635 (absorvida por #730) |

## Positivos preservados (R4 — candidatos a teste de proteção)

Arredondamento de parcelas; validação de movimento de caixa (rejeita zero/negativo);
escape de XSS em Audit Logs e busca; mascaramento LGPD; auditoria rica; painel de Operação.
Nenhum regrediu — cobertura de proteção a agendar por issue conforme cada área for tocada.

## Itens deferidos da missão (R0: propor, não inventar)

- **Mutação (Stryker):** não instalado; #754 já agenda como issue própria (meta 60% núcleo).
- **OpenTelemetry:** há `EasyStock.Api/Observability/`; confirmar se OTel está ligado antes de
  propor instrumentação de rotas — issue própria.
- **7 PRs paralelos:** superseded pelo CLAUDE.md (master-first, 1 issue/tarefa, 1 sessão/vez).
  Cada domínio vira issue + commits fatiados em master.
