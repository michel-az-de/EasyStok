# Inventário de Componentes — Shell Modular v6

## 1. O que o EasyStok.Web JÁ TEM (nada é reinventado)

### Design System (CSS — tudo com dark mode)

| Arquivo | Linhas | O que tem | Dark mode |
|---|---|---|---|
| `tokens.css` | 725 | Todas as cores (navy, orange, status), fontes, spacing, shadows, motion, focus rings, z-index, icon sizing | ✅ Bloco `[data-theme="dark"]` completo |
| `components.css` | 1227 | Buttons (6 variantes), Switch, Checkbox/Radio, Avatar, Badge (6 variantes), Stat card, Tooltip, Modal (4 sizes), Tabs, Accordion, Drawer, Dropzone, Stepper, Pagination, Combobox, Popover, Bulk bar, Skeleton, Empty state, Card, Sparkline, Focus rings, Status pills, Alerts, Validity pills, Table sort headers, Animations (fadeInUp, slideDown, scaleIn) | ✅ Overrides `[data-theme="dark"]` em toda a cascata |
| `app-shell.css` | 336 | App grid, Sidebar (com rail 72px), Topbar, Page header, Panel, Table (tbl), Filters/chips, Empty state, Responsive (tablet/mobile) | ✅ Usa tokens |
| `app.css` | ~ | Layout principal, sidebar estilos, topbar scroll state, animations | ✅ Usa tokens |
| `mobile.css` | ~ | Viewport <320px warning, mobile overrides | ✅ Usa tokens |

### TagHelpers (componentes Razor reutilizáveis)

| TagHelper | Onde usa | Status |
|---|---|---|
| `<es-sidebar>` | `_Sidebar.cshtml` | ✅ Operacional — renderiza menu com favoritos, badges, grupos accordion |
| `<es-badge>` | Várias views | ✅ Operacional |
| `<es-button>` | Várias views | ✅ Operacional — primary/navy/secondary/ghost/danger/success |
| `<es-icon>` | Todo lugar (Lucide SVG) | ✅ Operacional — 5 tamanhos, stroke 2 |
| `<es-kbd>` | Topbar, Cheatsheet | ✅ Operacional |
| `<es-stat-card>` | Dashboard, Analytics | ✅ Operacional — com sparkline |
| `<es-status-pill>` | Pedidos, Estoque | ✅ Operacional |

### Partials reutilizáveis

| Partial | O que faz | Usado em |
|---|---|---|
| `_Layout.cshtml` | HTML base, theme script, Alpine, CSS pipeline, app grid | Todas as páginas |
| `_Topbar.cshtml` | Busca Ctrl+K (funcional), tema, notificações, ações rápidas, avatar | Todas as páginas |
| `_Sidebar.cshtml` | Sidebar com logo, seletor de loja, `<es-sidebar>`, footer | Todas as páginas |
| `_BottomNav.cshtml` | Nav mobile | Todas as páginas |
| `_EmptyState.cshtml` | Ilustração SVG + título + descrição + CTA | Dashboard, Estoque, etc |
| `_Skeleton.cshtml` | 4 variantes: text, media, cards, rows | Views com loading |
| `_Toast.cshtml` | Sistema de toast | Todas as páginas |
| `_ConfirmModal.cshtml` | Modal de confirmação reutilizável | Todas as páginas |
| `_FormField.cshtml` | Campo de formulário padronizado | Formulários |
| `_Pagination.cshtml` | Paginação | Listagens |
| `_Tabs.cshtml` | Abas | Detalhes |
| `_Stepper.cshtml` | Stepper de progresso | Wizards |
| `_Cheatsheet.cshtml` | Atalhos de teclado (`?`) | Todas as páginas |
| `_LogoEasyStock.cshtml` | Logo SVG (cubo + wordmark) | Sidebar, Login |

### JS reutilizável (~/js/)

| Script | O que faz |
|---|---|
| `toast.js` | Toast global (success/error/warning/info) |
| `confirm.js` | Modal de confirmação |
| `keybindings.js` | Atalhos de teclado |
| `shortcuts.js` | Cheatsheet `?` |
| `format.js` | Formatação de moeda, data, número |
| `masks.js` | Máscaras de input |
| `form-guard.js` | Aviso de formulário sujo |
| `form-submit.js` | Loading state em botões |
| `form-validate.js` | Validação client-side |
| `menu-badges.js` | Polling de badges do menu |
| `menu-sidebar.js` | Interatividade do sidebar (pin, accordion, rail) |
| `nav-progress.js` | Barra de progresso no topo |
| `notifications.js` | Dropdown de notificações |
| `es-fetch.js` | Fetch wrapper com CSRF |
| `api.js` | Cliente API |
| `locale.js` | Locale pt-BR |
| `cep-autofill.js` | Autocomplete de CEP |
| `bulk.js` | Ações em massa |
| `row-nav.js` | Navegação por teclado em tabelas |

---

> **Correção (2026-08-08, #1007).** A linha do rail abaixo estava errada e a implementação
> não a seguiu. As classes `.app`/`.side`/`.side-item` de `app-shell.css:266-286` são
> **mortas**: nenhuma view as usa (só a galeria `Views/Dev/Components.cshtml`). O rail vivo é
> `html.es-rail` em `app.css:645-698`, a **64px**, e já existe desde a fatia 7 do ADR-0032 —
> como **preferência togglável de dispositivo** (`localStorage['es:rail']`).
> Torná-lo permanente contradiz o PATCH-1 daquele ADR e exigiria um flyout que o
> `menu-sidebar.js` evitou de propósito; por isso foi **descartado** (ver ADR-0046).
> O rail togglável convive com o shell modular sem ajuste nenhum.

## 2. O que o protótipo v6 traz de NOVO

| Conceito v6 | Componente real que mapeia | Esforço de adaptação |
|---|---|---|
| ~~**Rail lateral 72px**~~ (descartado) | ~~`app-shell.css` já tem `.side` com rail~~ — CSS morto; o rail vivo é `html.es-rail` (64px, togglável) | Não feito — ver nota acima |
| **Cards de módulo no launcher** | `es-stat-card` + `card-hover` em components.css | Baixo — usar TagHelper existente |
| **Tabela premium** | `.tbl` em app-shell.css + `.fin-table` em components.css | Baixo — estilos já existem |
| **Drawer lateral** | `.drawer-panel` em components.css | Baixo — CSS pronto |
| **Command Palette (Ctrl+K)** | `_Topbar.cshtml` já tem busca unificada funcional | Baixo — repaginar visual |
| **Toast com Desfazer** | `toast.js` + `_Toast.cshtml` já existem | Baixo — adicionar botão de desfazer |
| **Empty state ilustrado** | `_EmptyState.cshtml` já existe com SVG | Zero — usar partial existente |
| **Skeleton loader** | `_Skeleton.cshtml` já existe | Zero — usar partial existente |
| **Status pills** | `<es-status-pill>` + `.stp-*` em components.css | Zero — usar componente existente |
| **Badge com contagem** | `<es-badge>` + `.badge-*` em components.css | Zero — usar componente existente |
| **Avatar** | `.avatar` em components.css | Zero — usar classe existente |
| **Button variants** | `<es-button>` com 6 variantes | Zero — usar TagHelper existente |
| **Modal** | `.modal-panel` em components.css | Baixo — CSS pronto |
| **Focus rings** | `:focus-visible` em components.css | Zero — já existe |
| **Dark mode** | `[data-theme="dark"]` em tokens.css + components.css | Zero — já existe e funciona |
| **Animações de entrada** | `anim-fadeInUp`, `anim-scaleIn` em components.css | Baixo — adicionar stagger |
| **Gameficação (missões/XP)** | ❌ Não existe | Médio — nova funcionalidade, mas só JS + CSS |
| **Count-up nos números** | ❌ Não existe | Baixo — ~20 linhas de JS |
| **Partículas no login** | ❌ Não existe | Baixo — ~30 linhas de CSS/JS |
| **Ripple no botão** | ❌ Não existe | Baixo — ~15 linhas de JS |
| **Login glassmorphism** | `auth-premium.css` existe mas é diferente | Médio — adaptar estilo |

**Resumo: ~80% do visual do v6 já existe no sistema real.** O que falta são microinterações (partículas, ripple, count-up, gameficação) que são puro JS/CSS e não tocam arquitetura.

---

## 3. Todas as telas do sistema (40+ views)

### Operação
| View | Rota | Controller |
|---|---|---|
| Pedidos/Index.cshtml | `/pedidos` | PedidosController |
| Pedidos/Detail.cshtml | `/pedidos/{id}` | PedidosController |
| Pedidos/Recibo.cshtml | `/pedidos/{id}/recibo` | PedidosController |
| Pedidos/Etiqueta.cshtml | `/pedidos/{id}/etiqueta` | PedidosController |
| PedidosMobile/Index.cshtml | `/pedidos-mobile` | PedidosMobileController |
| Kds/Index.cshtml | `/kds` | KdsController |
| Caixa/Index.cshtml | `/caixa` | CaixaController |
| Caixa/Historico.cshtml | `/caixa/historico` | CaixaController |
| CaixaMobile/Index.cshtml | `/caixa-mobile` | CaixaMobileController |
| Clientes/Index.cshtml | `/clientes` | ClientesController |
| Clientes/Detail.cshtml | `/clientes/{id}` | ClientesController |
| ClientesMobile/Index.cshtml | `/clientes-mobile` | ClientesMobileController |
| Cardapio/Index.cshtml | `/cardapio` | CardapioController |
| Cardapio/Form.cshtml | `/cardapio/novo` | CardapioController |

### Produção e Estoque
| View | Rota | Controller |
|---|---|---|
| Estoque/Index.cshtml | `/estoque` | EstoqueController |
| Estoque/Detail.cshtml | `/estoque/{id}` | EstoqueController |
| Lotes/Index.cshtml | `/lotes` | LotesController |
| Lotes/Detail.cshtml | `/lotes/{id}` | LotesController |
| Lotes/Imprimir.cshtml | `/lotes/{id}/imprimir` | LotesController |
| LotesMobile/Index.cshtml | `/lotes-mobile` | LotesMobileController |
| Entradas/Historico.cshtml | `/entradas/historico` | EntradasController |
| Entradas/Nova.cshtml | `/entradas/nova` | EntradasController |
| Entradas/Reposicao.cshtml | `/entradas/reposicao` | EntradasController |
| Saidas/Historico.cshtml | `/saidas/historico` | SaidasController |
| Saidas/Nova.cshtml | `/saidas/nova` | SaidasController |
| Produtos/Index.cshtml | `/produtos` | ProdutosController |
| Produtos/Form.cshtml | `/produtos/novo` | ProdutosController |
| Produtos/Detail.cshtml | `/produtos/{id}` | ProdutosController |
| Categorias/Index.cshtml | `/categorias` | CategoriasController |
| Etiquetas/Editor.cshtml | `/etiquetas/editor` | EtiquetasController |
| Etiquetas/Modelos.cshtml | `/etiquetas/modelos` | EtiquetasController |
| MobileProducts/Index.cshtml | `/mobile-products` | MobileProductsController |
| MobileProducts/Divergencias.cshtml | `/mobile-products/divergencias` | MobileProductsController |

### Compras
| View | Rota | Controller |
|---|---|---|
| ListasCompras/Index.cshtml | `/listas-compras` | ListasComprasController |
| ListasCompras/Gerar.cshtml | `/listas-compras/gerar` | ListasComprasController |
| ListasCompras/Detail.cshtml | `/listas-compras/{id}` | ListasComprasController |
| ListasCompras/Imprimir.cshtml | `/listas-compras/{id}/imprimir` | ListasComprasController |
| ListasCompras/PedidosGerados.cshtml | `/listas-compras/pedidos-gerados` | ListasComprasController |
| Fornecedores/Index.cshtml | `/fornecedores` | FornecedoresController |
| Fornecedores/Detail.cshtml | `/fornecedores/{id}` | FornecedoresController |
| Fornecedores/PedidosAbertos.cshtml | `/fornecedores/pedidos-abertos` | FornecedoresController |

### Financeiro
| View | Rota | Controller |
|---|---|---|
| Financeiro/Index.cshtml | `/financeiro` | FinanceiroController |
| Financeiro/FluxoCaixa.cshtml | `/financeiro/fluxo-caixa` | FinanceiroController |
| ContasAReceber/Index.cshtml | `/contas-a-receber` | ContasAReceberController |
| ContasAReceber/Detail.cshtml | `/contas-a-receber/{id}` | ContasAReceberController |
| ContasAPagar/Index.cshtml | `/contas-a-pagar` | ContasAPagarController |
| ContasAPagar/Detail.cshtml | `/contas-a-pagar/{id}` | ContasAPagarController |
| NotasFiscais/Index.cshtml | `/notas-fiscais` | NotasFiscaisController |
| NotasFiscais/Detalhes.cshtml | `/notas-fiscais/{id}` | NotasFiscaisController |
| NotasFiscais/Emitir.cshtml | `/notas-fiscais/emitir` | NotasFiscaisController |

### Crescimento
| View | Rota | Controller |
|---|---|---|
| Dashboard/Index.cshtml | `/dashboard` | DashboardController |
| Analytics/Index.cshtml | `/analytics` | AnalyticsController |
| Inteligencia/Index.cshtml | `/inteligencia` | InteligenciaController |
| InteligenciaLojas/Index.cshtml | `/inteligencia-lojas` | InteligenciaLojasController |
| InteligenciaLojas/Detalhe.cshtml | `/inteligencia-lojas/{id}` | InteligenciaLojasController |
| Relatorios/Index.cshtml | `/relatorios` | RelatoriosController |
| Relatorios/Detail.cshtml | `/relatorios/{id}` | RelatoriosController |
| Anuncios/Index.cshtml | `/anuncios` | AnunciosController |

### Administração
| View | Rota | Controller |
|---|---|---|
| Configuracoes/Index.cshtml | `/configuracoes` | ConfiguracoesController |
| ConfiguracaoFiscal/Index.cshtml | `/configuracao-fiscal` | ConfiguracaoFiscalController |
| Usuarios/Index.cshtml | `/usuarios` | UsuariosController |
| Lojas/Index.cshtml | `/lojas` | LojasController |
| Dispositivos/Index.cshtml | `/dispositivos` | MobileDevicesController |
| Dispositivos/Backups.cshtml | `/dispositivos/backups` | MobileDevicesController |
| Notificacoes/Index.cshtml | `/notificacoes` | NotificacoesController |
| Preferencias/Index.cshtml | `/preferencias` | PreferenciasController |
| Assinatura/Index.cshtml | `/assinatura` | AssinaturaController |

### Auth
| View | Rota | Controller |
|---|---|---|
| Auth/Login.cshtml | `/auth/login` | AuthController |
| Auth/Registrar.cshtml | `/auth/registrar` | AuthController |
| Auth/SelecionarLoja.cshtml | `/auth/selecionar-loja` | AuthController |
| Auth/RedefinirSenha.cshtml | `/auth/esqueci-senha` | AuthController |

### Site (landing)
| View | Rota | Controller |
|---|---|---|
| Site/Index.cshtml | `/` | SiteController |
| Site/Precos.cshtml | `/precos` | SiteController |
| Site/Contato.cshtml | `/contato` | SiteController |
| Site/App.cshtml | `/app` | SiteController |
| Site/Faq/* | `/faq/*` | FaqController |

---

## 4. Decisão: vale a pena refatorar?

**SIM. E o escopo é menor do que parece.**

### O que realmente muda (novo)

| # | O que | Arquivos novos | Estimativa |
|---|---|---|---|
| 1 | **Launcher** (portal de módulos) | `LauncherController.cs`, `Launcher/Index.cshtml` | 2-3 dias |
| 2 | **Escolha de empresa** (se não houver uma só) | `EmpresaController.cs` ou modal no launcher | 1-2 dias |
| 3 | **Rail lateral** (72px fixo) | Ajuste em `_Sidebar.cshtml` + CSS | 1-2 dias |
| 4 | **Menu por módulo** (filtrar grupos) | `MenuViewModelBuilder.Build()` + parâmetro opcional | 1-2 dias |
| 5 | **Redirect pós-login** | `AuthController.SafeRedirect()` — 1 linha | 1 hora |
| 6 | **Gameficação leve** (missões) | Nova tabela + serviço + JS | 3-4 dias |
| 7 | **Microinterações** (count-up, ripple, stagger) | `animations.js` — puro CSS/JS | 1-2 dias |
| 8 | **Polimento dark mode** | Já existe — só garantir cobertura | 1 dia |

**Total: 10-14 dias de trabalho focado.**

### O que NÃO muda (zero risco)

- Todas as 40+ views existentes continuam iguais
- Todas as rotas (`/pedidos`, `/estoque`, `/caixa`, etc.)
- Todos os controllers
- Todo o CSS existente (tokens, components, app-shell)
- Todos os TagHelpers
- Todos os partials
- Todo o JS existente
- O login (`Auth/Login.cshtml`) — já é premium
- O Dashboard — vira tela interna do módulo Crescimento

### O que ganha

1. **Menos ruído**: de 25 itens de menu → 5-8 módulos no launcher → 4-6 itens por módulo
2. **Contexto preservado**: dentro do Financeiro, só vê coisas de financeiro
3. **Launcher como cockpit**: resumo do dia antes de entrar em qualquer módulo
4. **Identidade forte**: Casa da Babá e FMA Informática com seus próprios módulos
5. **Gameficação**: missões do dia guiam o operador
6. **Zero quebra**: nenhuma rota muda, nenhuma tela quebra

### Recomendação

Fazer como **um único PR de 10-14 dias**, fatiado:

- **Semana 1**: Launcher + rail + menu por módulo (itens 1-5)
- **Semana 2**: Gameficação + microinterações + polimento (itens 6-8)

As telas internas (tabela de pedidos, estoque, financeiro) **não precisam ser redesenhadas agora** — o shell modular já entrega 80% da melhoria de UX. O redesign das telas internas entra como épico futuro.
