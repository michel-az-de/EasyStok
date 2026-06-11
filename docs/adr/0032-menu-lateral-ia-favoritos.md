# ADR-0032: Menu lateral por fluxo, com "Meu dia" (favoritos no backend) e badges

**Status:** Accepted  
**Data:** 2026-06-10  
**Deciders:** Felipe Azevedo  
**Issue:** a abrir (backfill — api.github.com indisponível nesta sessão; autorizado iniciar sem a issue, "vai sem por enquanto")  
**Plano de execução:** `~/.claude/plans/o-easystok-multi-tenant-saas-replicated-coral.md` (v2.1, 2 rodadas de review `/architecture`)

---

## Contexto

O sidebar (`EasyStock.Web/Views/Shared/_Sidebar.cshtml`) tem 9 seções e ~24 itens **hardcoded em Razor, sempre expandidos**, com estado ativo via `ViewBag.ActiveMenuItem` (string setada à mão em cada controller, com muitos aliases). Exige scroll/cliques demais e não reflete o ciclo diário do tenant principal (fábrica de massas com delivery: pedido → produção/KDS → lote com validade → giro → reposição).

O `EasyStock.Web` é um **BFF HTTP puro** (sem `ProjectReference` a Domain/Application/Infra). Não há ViewComponent no projeto; TagHelpers existem e são testados via `Process()`. Persistência de preferência de usuário hoje é só tema (`Usuario.TemaPreferido`) e consentimentos de notificação — não há store genérico de UI.

---

## Decisão

### 1. Nova arquitetura de informação (nenhuma rota muda, exceto Config Fiscal)

| Grupo (accordion) | Itens |
|---|---|
| **Operação** | Pedidos (badge: pedidos em aberto) · KDS Operação · KDS Visor (ALPHA) · Caixa · Clientes |
| **Produção e estoque** | Lotes e validade (← Lotes; badge: lotes vencidos) · Posição de estoque (← Estoque; badge: críticos) · Entradas · Saídas · Produtos · Categorias |
| **Compras** | Pedidos de compra (← Compras) · Fornecedores |
| **Financeiro** | Visão geral (← Financeiro) · Contas a receber · Contas a pagar · Notas fiscais |
| **Crescimento** | Análises · Relatórios · Anúncios |

Topo fixo: seletor de loja (mantido) · gatilho de busca Ctrl K (reusa command palette existente) · Dashboard (badge vermelho = soma de alertas). Rodapé fixo: Dispositivos · Usuários · Configurações · versão. **Config Fiscal sai do menu** e vira aba em Configurações (redirect 302 da rota antiga).

### 2. Estrutura como dado: POCO + builder puro + TagHelper com DI

`MenuDefinition` (estático, `EasyStock.Web/Navigation/`) é a fonte única — renomear/reordenar = editar lá. `MenuViewModelBuilder` (puro, sem HttpContext) concentra a lógica testável. `EsSidebarTagHelper` (`ProcessAsync` + DI) orquestra os services e renderiza — **não** via `BaseController`/filtro global (evita `.Result` síncrono, fetch inútil nos endpoints JSON e dependência de herança).

Chaves de item são **kebab-case estáveis**; renomear uma chave orfaniza o favorito que a referencia (o builder descarta órfãos).

### 3. Matching ativo-por-rota

Por **segmentos** de path (nunca prefixo cru de string — `/pedidos` não casa `/pedidos-kds`); case-insensitive; ignora querystring/fragmento e trailing slash; vence o match de **mais segmentos**; **fallback** para `ActiveMenuItem` legado quando nenhum href casa; nenhum match = nada ativo, nenhum grupo aberto (Meu dia visível). A rota **sempre** vence o `ActiveMenuItem` quando ambos resolvem.

### 4. "Meu dia" (favoritos) persistido por usuário + loja

Nova entidade `PreferenciaMenuUsuario` (PK `Id`, único `(UsuarioId, LojaId)`, `EmpresaId`, `FavoritosJson : List<string>` em `jsonb` **com `ValueComparer`** — o molde `FaturaConfiguration` NÃO tem comparer, é bug latente a sinalizar). Contrato **claims-only** (`/api/preferencias/menu-favoritos`): UsuarioId/EmpresaId vêm de `CurrentUserAccessor` (claim `sub`/`empresaId`), `lojaId` validado contra a empresa — **sem IDOR**. `GET` devolve `{favoritos: null|[...], kdsHabilitado}` (null ⇒ Web aplica seed; `[]` ⇒ seção some). `PUT` faz upsert (descarta chaves desconhecidas, cap 20, lista nova, catch de unique-violation → reler+atualizar). BFF expõe só o `PUT /menu/favoritos` (sem GET público — o TagHelper renderiza e o Alpine hidrata do JSON inline). Cache 5min `fav:{usuarioId}:{lojaId}`, invalidado no PUT (limitação conhecida: staleness de até 5min da flag no menu; seguro com 1 instância Web — revisar se escalar para 2+).

Pipeline do builder na ordem (1) filtra a árvore por `KdsHabilitado` (itens `IsProducaoKds` somem quando off) → (2) resolve favoritos contra a árvore filtrada e descarta órfãos. Favorito de KDS gravado com a flag ligada some sozinho quando ela desliga.

### 5. Flag `Loja.KdsHabilitado` e seed do "Meu dia"

Nova coluna `Loja.KdsHabilitado` (default `false`, exposta no `LojaApi`, toggle em Configurações). Seed por perfil: com KDS `[pedidos, kds-operacao, lotes-validade, posicao-estoque]`; sem KDS `[pedidos, posicao-estoque]`. Não persiste no 1º acesso (builder devolve o default; grava só no 1º pin/unpin/reorder).

### 6. Badges via BFF cacheado

`GET /menu/resumo` (Web) agrega `analytics/dashboard` (`AlertasEstoqueBaixo`=críticos, `AlertasVencidos`=lotes vencidos) + `analytics/dia` (`PedidosPendentes`), `IMemoryCache` 60s key `empresaId:lojaId` (sem loja = vazamento). Nunca cacheia falha. `menu-badges.js` (clone de `notifications.js`) faz poll 60s, atualiza todas as instâncias via `[data-menu-key]`. Dashboard = críticos+vencidos (**pedidos abertos não entram**). Badge de grupo (soma dos filhos) renderizado no server e escondido por CSS quando o grupo está aberto.

### 7. Decisões D1–D4 (ratificadas)

- **D1** Accordion **sem localStorage** — a rota decide qual grupo abre (exclusivo). Elimina o flash rota↔storage.
- **D2** Cache de favoritos 5min + invalidação no PUT (1 máquina Web hoje; caveat de escala).
- **D3** Sem GET BFF público de favoritos (server renderiza + Alpine hidrata inline; PUT é o único write).
- **D4** Reorder = drag-drop HTML5 no desktop (`pointer:fine`) + menu de contexto "mover ↑/↓" (alternativa teclado/touch). 1 PUT no fim (debounce).

### 8. Correções de arquitetura (PATCH-1 / PATCH-2)

- **PATCH-1:** rail (~64px) é preferência de **dispositivo** — localStorage chave única `es:rail`, **sem scope por usuário+loja**; classe aplicada por script inline no `<head>` antes da pintura. `data-pref-scope` descartado.
- **PATCH-2:** o VM da aba fiscal em Configurações é composto **eager** por um `MontarIndexVmAsync()` privado, usado pelo GET `Index` **e** pelo caminho de erro de `Salvar` (evita view morta). Os 6 POSTs fiscais permanecem (já fazem PRG) com o redirect repontado para `/configuracoes?tab=fiscal`.

---

## Tabela alias → item (inventário de `ActiveMenuItem`, grep dos controllers — fatia 1)

| Item (key) | ActiveKeys (valores legados emitidos) |
|---|---|
| dashboard | Dashboard |
| pedidos | Pedidos, PedidosMobile |
| kds-operacao | Kds |
| caixa | Caixa, CaixaMobile |
| clientes | Clientes, ClientesMobile |
| lotes-validade | Lotes, LotesMobile |
| posicao-estoque | Estoque |
| entradas | Entradas |
| saidas | Saidas |
| produtos | Produtos, ProdutosMobile |
| categorias | Categorias |
| pedidos-compra | ListasCompras |
| fornecedores | Fornecedores |
| financeiro | Financeiro |
| contas-receber | ContasAReceber |
| contas-pagar | ContasAPagar |
| notas-fiscais | NotasFiscais |
| analises | Analytics, Movimentacoes, Inteligencia, InteligenciaLojas |
| relatorios | Relatorios |
| anuncios | Anuncios |
| dispositivos | Dispositivos, Operacao |
| usuarios | Usuarios |
| configuracoes | Configuracoes, Lojas, Assinatura, Notificacoes, Preferencias, ConfiguracaoFiscal |

`kds-visor` é externo (PWA, `target=_blank`) — sem ActiveKeys. Config Fiscal mapeia em `configuracoes` (vira aba). Aliases mobile (ex.: `ProdutosMobile`) mapeiam ao item desktop equivalente — pequena mudança de comportamento vs. menu antigo, intencional. Um teste garante que todo alias do inventário resolve a um item (alias órfão falha o build).

---

## Fatiamento (commits build-verdes; WIP=1)

| Fatia | Conteúdo |
|---|---|
| 0 | ADR-0032 (este) + issue (backfill) |
| 1 | `MenuDefinition` + `MenuViewModelBuilder` + testes unitários |
| 2 | Badges BFF (`/menu/resumo` + `menu-badges.js`) |
| 3 | Flag `Loja.KdsHabilitado` (coluna + migration + `LojaApi` + toggle) |
| 4 | Persistência favoritos (entidade + migration + API + BFF) |
| 5 | `EsSidebarTagHelper` (render isolado + testes `ProcessAsync`) |
| 6 | Swap do `_Sidebar.cshtml` |
| 7 | Interatividade Alpine (pin/unpin, accordion, rail, reorder) |
| 8 | Config Fiscal → aba + redirect 302 |
| 9 | A11y / Lighthouse ≥95 / limpeza |

---

## Consequências

**Positivas:** menu reflete o fluxo operacional; navegação sem scroll com "Meu dia"; estado ativo derivado da rota (robusto a aliases); estrutura testável (POCO + builder puro); favoritos sobrevivem a troca de dispositivo; badges consistentes com o painel "Atenção".

**Negativas / aceitas:** 2 migrations novas (`KdsHabilitado`, `PreferenciaMenuUsuario`) — exigem `.Designer.cs` e `dotnet ef database update` (autorização à parte); staleness de até 5min da flag no menu (cache); cache de favoritos por instância (revisar se Web escalar para 2+ máquinas); `ActiveMenuItem` mantido como fallback durante a transição (limpeza na fatia 9); bug latente do `FaturaConfiguration` (sem `ValueComparer`) apenas sinalizado, não corrigido (WIP=1).

---

## Referências

- ADR-0019 (mobile controllers response pattern), ADR-0022 (master-first), ADR-0029 (poka-yoke / build-check)
- Plano v2.1 (acima) — verificações de repositório 1–9 e PATCH-1/PATCH-2
- `FaturaConfiguration` (molde jsonb), `DashboardController` (molde de agregação), `notifications.js` (molde de polling), `_Tabs.cshtml` (abas)
