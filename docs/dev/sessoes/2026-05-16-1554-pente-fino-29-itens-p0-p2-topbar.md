# Sessao pente-fino 29 itens (P0 + P2 topbar)

Data: 2026-05-16 ~14:30-15:54 UTC-3
Worktree: master principal (C:/rep/EasyStok)
Identidade Git: felipe.azevedo@gmail.com / michel-az-de
Status final: parcial (2 PRs abertas; 22 dos 29 achados ainda sem fix)

## O que foi feito

### Triagem completa dos 29 achados (relatado pelo Felipe via navegacao manual)

3 agentes Explore em paralelo leram o codigo do EasyStock.Web e classificaram cada item:

| Classificacao | Achados |
|---|---|
| Bug real confirmado | #1, #2, #3*, #5, #6, #9*, #12/29, #14, #19, #20, #23, #27, #28 |
| Falso positivo | #4 (overlay Claude in Chrome), #10 (variacoes SKU), #11 (cadastro parcial intencional) |
| Nao verificavel via static analysis | #7, #8, #13, #15, #16, #17, #18, #21, #22, #24, #25, #26 |

(*) #3 e #9 marcados como possivel falso positivo / precisam runtime.

### PR #153 — P0 dashboard + sidebar + diagnostico

Branch `fix/web-pente-fino-p0-dashboard-sidebar`, commit `c055a992`.

**Arquivos:**
- `EasyStock.Web/Views/Dashboard/Index.cshtml` (3 bugs Alpine)
- `EasyStock.Web/Views/Shared/_Sidebar.cshtml` (logo + link dispositivos)
- `EasyStock.Web/Controllers/DiagnosticoController.cs` (Authorize Roles)

**Fixes:**
- #2 Alpine: `alertasFiltrados` virou getter (`.slice`/`.length` falhavam). `contarAlerta` renomeado para `contarTipo` (template chamava com nome errado). `<template x-if>` dentro de `<svg>` substituido por `<path x-show>` (Alpine quebrava com cloneNode em template+svg).
- #1 (parcial): link `/dispositivos` removido (DispositivosController nao existe).
- #28: "EasyStok" -> "EasyStock" no logo da sidebar.
- #27: `/diagnostico` (Index) restrita a Admin/SuperAdmin.

### PR #154 — Topbar a11y + nowrap

Branch `fix/web-topbar-a11y-nowrap`, commit `cd1f3780`.

**Arquivos:**
- `EasyStock.Web/Views/Shared/_Topbar.cshtml`

**Fixes:**
- #19: badge do sino com `role=status`, `aria-live=polite`, `aria-atomic`, `aria-label`. Botao com `aria-label`, `aria-haspopup`, `aria-expanded` reativo.
- #20: `whitespace-nowrap` adicionado a `theme-toggle-btn` e ao botao "Acao rapida". Resolve quebra de linha em containers apertados.

## O que ficou pendente

### P0/P1 nao tratados nesta sessao

| # | Item | Status |
|---|---|---|
| #1 /lotes 500 | Codigo limpo, sem causa via static analysis. Precisa **trace ID dos logs do Fly** ou repro local. | aberto |
| #5 Dashboard skeleton trava apos navegacao | Fetch `/dashboard/data` em Index.cshtml:993. `finally()` nao limpa loading em timeout silencioso. Fix: tratar Promise.race com timeout. | aberto |
| #6 KPI "Itens em estoque" 0,0% vs 100% lotes critico | `DashboardAnalyticsQueries.cs:201-202` mistura unidades (sum qty) e lotes (count rows). | aberto |
| #12/29 50 lotes (dashboard) vs 67 (estoque) | Scope diferente: KPI filtra finalizados ultimos 30d, /estoque snapshot atual. Adicionar rotulo. | aberto |
| #14 KDS "70h58" | `tempoLabel()` em Views/Kds/Index.cshtml sem breakpoint >24h. | aberto |
| #15 Pedidos "Entregue sem cobranca" | Views/Pedidos/Index.cshtml — confirmar se feature ou bug de status. | nao verificado |
| #23 Custo R$0 em PRODUCAO | Backend nao popula `Custo` para movimentacoes de producao. Fix maior (esforco alto). | aberto |

### P2 cosmeticos nao tratados

| # | Item | Esforco |
|---|---|---|
| #9 tema escuro label | Codigo PARECE correto (refreshToggle atualiza textContent). Provavel cache SW. Confirmar Ctrl+F5. | trivial |
| #7, #8 graficos validades/eixo Y | Precisa runtime + biblioteca de chart usada. | medio |
| #16 titulos invisiveis em /relatorios | Provavel classe tailwind text-* errada. | baixo |
| #17 empty-state Fornecedores | Era overlay Claude in Chrome — provavel falso positivo. | nenhum |
| #18 cursor SVG sobre R$0 em /caixa | Precisa inspecao runtime. | baixo |
| #21 email mobile-sync exposto | Filtrar usuarios de sistema em /usuarios. | baixo |
| #22 placeholder data inconsistente | Saidas/Entradas — campo "ate" sem pre-fill. | baixo |
| #24 "88% lotes criticos" baixo contraste | Dashboard alerta final. | baixo |
| #25 grafico Thatiane sem contexto | Pode ser falta de titulo de eixo. | baixo |
| #26 seta dupla loja selecionada | Sidebar so tem 1 chevron no codigo — possivel falso positivo. | nenhum |
| #13 regra Atencao/Critico | Regra nao localizada nos services Razor; pode estar no domain enum/migration. | medio |

### Falsos positivos (nao precisam acao)

- #3 "Tela muito estreita": CSS `@media max-width:319px` esta correto. Aviso provavelmente vinha do overlay do Claude in Chrome.
- #4 botao "Stop Claude" sobreposto: e o overlay do MCP Claude in Chrome, nao bug da PWA.
- #10 Ravioli 3x: variacoes de SKU/tamanho (design).
- #11 produto ATIVO sem preco: feature intencional (cadastro parcial).
- #21 (parcial): provavelmente backend gera; filtrar em frontend basta.

## Decisoes tomadas

- **/lotes 500 pulado** — sem logs/repro nao da pra localizar a exception. Felipe vai trazer o trace ID em sessao futura.
- **/dispositivos link removido** em vez de criar stub. DispositivosController vai vir junto com o modulo proprio.
- **/analises e /kds-visor** — sao typos do usuario (rotas reais sao /analytics e /pwa/#kds via apiBase). Sem acao.
- **Stop Claude overlay (#4)** — overlay do Claude in Chrome MCP. Sem acao.
- **#9 (label tema escuro)** — leitura do codigo mostra que e suposto funcionar. Provavel cache SW. Felipe confirma Ctrl+F5.
- **Husky aceito** — passou em ambos commits (rotulagem-architecture-tests).
- **Falha tolerada** — `Exceptions_De_Domain_Devem_Ficar_No_Domain` em flaky-tests.md continua falhando (regressao arquitetural desde 4b018b39). Toleramos.

## Commits criados

- `c055a992` fix(web): pente-fino p0 dashboard + sidebar + diagnostico [PR #153]
- `cd1f3780` fix(web): topbar a11y badge notif + nowrap nos botoes header [PR #154]

## Branches criadas

- `fix/web-pente-fino-p0-dashboard-sidebar` (PR #153, aberta)
- `fix/web-topbar-a11y-nowrap` (PR #154, aberta)

## Proxima acao recomendada

1. **Felipe mergea PR #153 e #154** (squash) apos validar em prod.
2. **Felipe traz trace ID do 500 em /lotes** — ai dah pra investigar a exception especifica.
3. **Triagem das P1 restantes** (#5, #6, #12, #14, #23) — cada uma vira PR pequena.
4. **Rodada cosmeticos #16, #18, #21, #22, #24** — agrupar em 1 PR.
5. **Decisao sobre #13 (regra Atencao/Critico)** — precisa Felipe dizer regra correta.

## Referencias

- PR #153: https://github.com/michel-az-de/EasyStok/pull/153
- PR #154: https://github.com/michel-az-de/EasyStok/pull/154
- flaky-tests.md: `Exceptions_De_Domain_Devem_Ficar_No_Domain` (tolerado)
- Handoff anterior: docs/dev/sessoes/2026-05-16-1245-fases-1-2-3-handoff-final.md
- ADRs relevantes: 0011 (nomenclatura PT-BR), 0013 (cancellation token)
