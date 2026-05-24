# Features EasyStok — mapa de modulos

> Sumario publico do que ja existe no codigo, organizado por modulo de negocio.
> Para detalhes tecnicos (entidades, use cases, conventions), consulte
> [`.knowledge/current-state.md`](../.knowledge/current-state.md) e
> [`.knowledge/domain-glossary.md`](../.knowledge/domain-glossary.md).

**Snapshot:** 2026-05-24  
**Mantenedor:** [@michel-az-de](https://github.com/michel-az-de)

## Legenda

| Status | Significado |
|---|---|
| ✅ Pronto | Implementado, testado, em uso real |
| 🟡 Parcial | Usavel com gaps conhecidos |
| 📋 Planejado | Especificado, com task ETK no backlog |
| ❌ Nao iniciado | Sem implementacao nem task |
| ⛔ Diferido | Decisao explicita de adiar |

## Indice

- [Identidade & Auth](#identidade--auth)
- [Multi-tenant](#multi-tenant)
- [Catalogo & Estoque](#catalogo--estoque)
- [Pedido & Venda](#pedido--venda)
- [Compras (Pedido Fornecedor)](#compras-pedido-fornecedor)
- [Caixa & Sessao](#caixa--sessao)
- [Financeiro (CAP/CAR)](#financeiro-capcar)
- [Billing & Assinatura SaaS](#billing--assinatura-saas)
- [Pagamentos (gateways)](#pagamentos-gateways)
- [NF-e / NFC-e / Fiscal](#nf-e--nfc-e--fiscal)
- [Rotulagem Nutricional](#rotulagem-nutricional)
- [Etiquetas Termicas](#etiquetas-termicas)
- [Notificacoes](#notificacoes)
- [Helpdesk & FAQ](#helpdesk--faq)
- [Mobile MAUI + PWA](#mobile-maui--pwa)
- [KDS — Kitchen Display](#kds--kitchen-display)
- [IA (Claude integration)](#ia-claude-integration)
- [Admin (back-office Easystok)](#admin-back-office-easystok)
- [Auditoria & LGPD](#auditoria--lgpd)
- [Onboarding & Landing](#onboarding--landing)
- [Webhooks & Idempotencia](#webhooks--idempotencia)
- [Infra cross-cutting](#infra-cross-cutting)

---

## Identidade & Auth

**Status:** ✅ Pronto

Auth completo com JWT 8h + refresh 30d, logout, reset senha, email confirmation,
biometria mobile, revogacao de tokens, rate limit `auth/*` (B-015), preserva
returnUrl em POST. Entidades chave: `Usuario`, `RefreshToken`, `ResetToken`,
`EmailConfirmationToken`, `Perfil`, `PerfilPermissao`, `UsuarioPerfil`.

Detalhes: [`.knowledge/current-state.md`](../.knowledge/current-state.md) > Auth.

## Multi-tenant

**Status:** ✅ Pronto + 🟡 leak test automatizado pendente

Empresa → Loja → Usuario → Perfil/Permissao com tenant guards em todos os
controllers + `ValidateEmpresaId` em body POST/PUT. Defesa em profundidade
via RLS Postgres ([ADR-0010](adr/0010-rls-postgres-defesa-em-profundidade.md))
ativada em prod, com gap pos-migration corrigido em 3 tabelas
([PR #211](https://github.com/michel-az-de/EasyStok/pull/211)).

`TenantFeatureFlag` permite feature flags por tenant. Multi-empresa por
usuario via `UsuarioEmpresa` (parcial, falta seletor global UI).

Pendente: teste automatizado de multi-tenant leak em todos os endpoints
(stability-roadmap Bloco 3).

## Catalogo & Estoque

**Status:** ✅ Pronto (Q% 49% por audit-brutal — gaps em variantes/grades)

Entidades: `Produto` (com VOs `Sku`, `Validade`, `Dinheiro`, `Gtin`,
`Cnpj`, `Telefone`, `EmailAddress`, `PasswordPolicy`), `Categoria`,
`Fornecedor`, `ItemEstoque`, `Lote`, `MovimentacaoEstoque`,
`ProdutoVariacao`, `ProdutoComposicao`, `ProdutoEmbalagem`,
`ProdutoCaracteristica`, `TipoEmbalagem`.

- **FEFO/FIFO determinismo real** ([#221](https://github.com/michel-az-de/EasyStok/pull/221)) com
  `IgnoreQueryFilters` em `FromSqlRaw FOR UPDATE` preservando ORDER BY
  ([#209](https://github.com/michel-az-de/EasyStok/pull/209), [#199](https://github.com/michel-az-de/EasyStok/pull/199), [#212](https://github.com/michel-az-de/EasyStok/pull/212)).
- **Idempotencia** em POSTs via `IdempotencyKey` (path-prefix configuravel).
- **Auditoria E2E LGPD/GDPR** via `ProdutoAlteracao`, `MovimentacaoEstoqueAlteracao`,
  `FornecedorAlteracao`.
- **VO Gtin** com checksum mod10 ([#156](https://github.com/michel-az-de/EasyStok/pull/156)).

Gap: UI de variantes/grades de produto nao exposta (estrutura existe).

## Pedido & Venda

**Status:** ✅ Pronto (Q% 56%)

`Pedido` com state machine matrix explicita
(`aguardando | preparando | pronto | entregue | cancelado`),
`ItemVenda`, `Venda`, `VendaAlteracao`. Idempotencia em movimentacao
de estoque via `MovimentacaoEstoque.DocumentoReferencia = "{pedidoId}:{itemId}"`.

- `PedidoEstoqueIntegrationService` extraido com `PedidoEstoqueOptions`.
- `AtualizarStatusPedidoUseCase` com state machine matrix explicita.
- `GetByIdWithDetailsAsync` evita N+1.
- `xmin` RowVersion em Pedido para concurrency control.

Detalhes: [`.knowledge/domain-glossary.md`](../.knowledge/domain-glossary.md) > Pedido vs Venda.

## Compras (Pedido Fornecedor)

**Status:** ✅ Pronto (3 fases consolidadas em maio 2026)

Entidades: `PedidoFornecedor`, `PedidoFornecedorItem`, `ListaCompras`.

- **Fase 1** ([#187](https://github.com/michel-az-de/EasyStok/pull/187)): lista de compras inteligente
- **Fase 2** ([#189](https://github.com/michel-az-de/EasyStok/pull/189)): virar lista em pedido de fornecedor
- **Fase 3** ([#190](https://github.com/michel-az-de/EasyStok/pull/190)): recebimento da entrada no estoque
- **UX da lista** ([#192](https://github.com/michel-az-de/EasyStok/pull/192)): marcar instantaneo, busca de produto, formatacao

Pendente: teste de integracao recebimento → entrada estoque (stability-roadmap
Bloco 3).

## Caixa & Sessao

**Status:** ✅ V1 pronta / 📋 V2 planejada / ⛔ V2 diferida

**V1 atual:** `MovimentoCaixa` e `FechamentoCaixa` funcionais, integrados com
Pedido e Venda. Caixa abre automatico best-effort no primeiro pagamento.

**V2 planejada** (`SessaoCaixa` como entidade explicita com state machine
`aberta → em_conferencia → fechada`, hash SHA-256, PDF QuestPDF, QR
verificacao publica): plano completo em
[`docs/plan/00-reconhecimento.md`](plan/00-reconhecimento.md) (8 documentos).

**Tasks reservadas no backlog (diferidas):**

- [ETK-0006 — Entity SessaoCaixa](tasks/backlog/ETK-0006-entity-sessao-caixa.yaml)
- [ETK-0007 — Entity MovimentoCaixa (expansao)](tasks/backlog/ETK-0007-entity-movimento-caixa.yaml)
- [ETK-0008 — Entity FechamentoCaixa (expansao)](tasks/backlog/ETK-0008-entity-fechamento-caixa.yaml)
- [ETK-0009 — UseCase AbrirSessaoCaixa](tasks/backlog/ETK-0009-usecase-abrir-sessao-caixa.yaml)
- [ETK-0010 — UseCase RegistrarMovimentoCaixa](tasks/backlog/ETK-0010-usecase-registrar-movimento-caixa.yaml)
- [ETK-0011 — UseCase FecharSessaoCaixa](tasks/backlog/ETK-0011-usecase-fechar-sessao-caixa.yaml)
- [ETK-0012 — Migration AddCaixaModule](tasks/backlog/ETK-0012-migration-add-caixa-module.yaml)

Decisao de diferir: [ADR-0021](adr/0021-rotulagem-p02-etapa5-do-roadmap.md) —
Rotulagem P-02 vence Caixa V2 como Etapa 5 do roadmap.

ADRs relacionadas:
[ADR-0014 (pagamento aditivo)](adr/0014-pagamento-aditivo-em-pedido-pagamento.md),
[ADR-0015 (SessaoCaixa entidade explicita)](adr/0015-sessao-caixa-como-entidade-explicita.md),
[ADR-0016 (hash + retencao 5 anos)](adr/0016-fechamento-hash-retencao-cinco-anos.md).

## Financeiro (CAP/CAR)

**Status:** ✅ Pronto (modulo completo em 5 ondas, [#88](https://github.com/michel-az-de/EasyStok/pull/88))

Contas a Pagar, Contas a Receber, `Lancamento`, `LancamentoBaixa`,
`CategoriasFinanceiras`, `CentrosCusto`. Combobox com criacao inline em
Categoria e Centro de Custo ([#186](https://github.com/michel-az-de/EasyStok/pull/186)).

Bootstrap `Lancamento + LancamentoBaixa` em [#127](https://github.com/michel-az-de/EasyStok/pull/127).

## Billing & Assinatura SaaS

**Status:** ✅ Pronto (Q% 47% por audit — F1-F14 onda billing concluida)

Entidades: `AssinaturaEmpresa`, `Plano`, `CobrancaAssinatura`, `Cupom`,
`Fatura`, `FaturaItem`, `FaturaPagamento`, `FaturaEvento`, `FaturaContador`.

- **Trial 14d + Pix Efí + gate middleware** retornando 402 para tenants
  suspensos.
- **`SubscriptionGateMiddleware` com cache** (TTL 60s, [#75](https://github.com/michel-az-de/EasyStok/pull/75))
  invalidado por interceptor EF em qualquer SaveChanges em `AssinaturaEmpresa`.
- **Dashboard financeiro** com MRR, ARR, churn, top inadimplentes (F10).
- **Estorno + consulta Efí** (F11).
- **PDF de fatura via QuestPDF** (F4).
- **Adapters Stripe + MercadoPago + signature validators** (F12).
- **Auto-ticket Financeiro** apos N falhas de pagamento (F14).
- **Webhook MercadoPago valida janela de replay** ([#79](https://github.com/michel-az-de/EasyStok/pull/79)).

Gap: rotacao automatica de planos quando muda (audit).

## Pagamentos (gateways)

**Status:** ✅ Pronto

`PedidoPagamento` (multi-pagamento por pedido), adapters Pix Efí + Stripe +
MercadoPago, `PaymentAttempt` + audit, smart routing
([#95 Payment Orchestration P0](https://github.com/michel-az-de/EasyStok/pull/95)).

- **Webhook Pix valida valor pago vs cobranca** (vuln R$0,01 fechada
  em commit `37fb7d9`).
- **HMAC-SHA256 + replay protection** em todos os webhooks.

Gap: race em duplo-fire dentro de janela 5min (stability-roadmap Bloco 5).

ADR: [ADR-0014 — pagamento aditivo em PedidoPagamento](adr/0014-pagamento-aditivo-em-pedido-pagamento.md).

## NF-e / NFC-e / Fiscal

**Status:** 🟡 Fundacao + 📋 emissao real planejada

- ✅ **Domain NFC-e Corte 1** (M-01 pavimentacao, [#85](https://github.com/michel-az-de/EasyStok/pull/85))
- ✅ **UX configuracao fiscal Admin + listagem NFC-e Web ERP** ([#169](https://github.com/michel-az-de/EasyStok/pull/169))
- ✅ **Operacao NF visual end-to-end no Web admin** ([#177](https://github.com/michel-az-de/EasyStok/pull/177))
- 📋 **Emissao real via Focus NFe ou eNotas** (planejada):
  - [ETK-0013 — NfeEventOutbox entity](tasks/backlog/ETK-0013-nfe-event-outbox-entity.yaml)
  - [ETK-0014 — UseCase EmitirNfe modelo 55](tasks/backlog/ETK-0014-usecase-emitir-nfe-modelo-55.yaml)
  - [ETK-0015 — Background service EmitirNfe](tasks/backlog/ETK-0015-background-emitir-nfe.yaml)

ADR: [ADR-0018 — nomenclatura Nfe* prefixo curto](adr/0018-nomenclatura-nfe-prefixo-curto.md).

## Rotulagem Nutricional

**Status:** 📋 Planejado (Etapa 5 do roadmap — [ADR-0021](adr/0021-rotulagem-p02-etapa5-do-roadmap.md))

- ✅ **UI ficha tecnica nutricional** ja existe ([#158](https://github.com/michel-az-de/EasyStok/pull/158)).
- 📋 **Modulo P-02 completo** (compliance Anvisa, IA extrai ficha):
  - [ETK-0016 — Entity RotuloNutricional](tasks/backlog/ETK-0016-entity-rotulo-nutricional.yaml)
  - [ETK-0017 — UseCase GerarRotuloNutricional](tasks/backlog/ETK-0017-usecase-gerar-rotulo.yaml)
- Plano: [`docs/plan/p-02-rotulagem-nutricional.md`](plan/p-02-rotulagem-nutricional.md).

ADRs:
[ADR-0011 (nomenclatura PT-BR)](adr/0011-nomenclatura-pt-br-rotulagem.md),
[ADR-0012 (backup storage)](adr/0012-backup-rotulos-storage.md),
[ADR-0017 (comprovante aprovacao interna RT)](adr/0017-comprovante-aprovacao-interna-rt.md).

## Etiquetas Termicas

**Status:** ✅ Pronto (Casa da Baba em producao)

`EtiquetaTemplate`, `EtiquetaTemplateSistema`, `EtiquetaEmpresaDefault`,
`PayloadHelpers`, render JS helpers. Suporte WTP05 + Bluetooth nativo via
plugin Casa da Baba PWA.

## Notificacoes

**Status:** ✅ Pronto (PR1-PR7 multi-canal)

Outbox pattern (nunca despachar inline). Adapters: Email, SMS, WhatsApp,
InApp. Worker dispatcher com heartbeat `/health/dispatcher` separado de
`/health/api` ([#72](https://github.com/michel-az-de/EasyStok/pull/72)).

- **Painel Admin** com seed 16 templates ([#126](https://github.com/michel-az-de/EasyStok/pull/126))
- **LGPD self-service** + metricas OTel
- **Pente fino Scriban** — sandbox real, timeout efetivo, cache, auto-escape HTML ([#89](https://github.com/michel-az-de/EasyStok/pull/89))

## Helpdesk & FAQ

**Status:** ✅ Pronto (E2E)

- **AdminTicket** reformado: `TicketAnexo`, `TicketHistorico`,
  `AdminTicketTecnicoMeta`, `SlaConfiguracao`.
- **SlaMonitorService** dispara `SlaProximoVencer` / `SlaViolado`.
- **9 eventos globais de notificacao**.
- **UI Tickets multi-nivel**.
- **Fluxo cliente E2E + dashboard + CSAT + relatorio** ([#82](https://github.com/michel-az-de/EasyStok/pull/82)).
- **FAQ E2E + CanalOrigem** (`FaqItem`, `FaqCategoria`, `FaqFeedback`,
  `FaqVisualizacao` — [#101](https://github.com/michel-az-de/EasyStok/pull/101)).

## Mobile MAUI + PWA

**Status:** ✅ MAUI F0-F4c + ✅ PWA Casa da Baba em producao

Politica dual-frontend formalizada em
[`.knowledge/dual-frontend-policy.md`](../.knowledge/dual-frontend-policy.md):
PWA em `EasyStock.Api/wwwroot/pwa/` e copiado para `EasyStok.Mobile/Resources/Raw/pwa/`
no mesmo commit + hash SHA-256 conferido.

**MAUI (`EasyStok.Mobile`):**
- F0 setup → F1 estrutura → F2 auth E2E (JWT+refresh+biometria) → F2b/c
  multi-tenant pickers + permissoes → F3 producao SQLite + REST → F4 mutations
  otimistas (outbox SQLite) → F4b SyncEngine → F4c popup captura
  foto/peso/validade.
- Sync via `MobileProcessedMutation`, `SyncController`, `SyncMutationDispatcher`.

**PWA Casa da Baba:**
- Scanner QR/Code128, conferencia rapida, etiquetas termicas WTP05,
  BT plugin nativo, LiveUpdater (Capacitor), offline-first.
- Calculadora de producao + receitas ([#135](https://github.com/michel-az-de/EasyStok/pull/135)).
- Ondas pente fino: rotas, KDS, filtros, badges, validacoes (4 ondas em maio).
- Onda 1 Clientes — score, recencia, WhatsApp, Maps ([#137](https://github.com/michel-az-de/EasyStok/pull/137)).

ADR: [ADR-0019 — mobile controllers response pattern](adr/0019-mobile-controllers-response-pattern.md).

## KDS — Kitchen Display

**Status:** 🟡 Alpha

Tela alpha de Kitchen Display System pra cozinha ([#84](https://github.com/michel-az-de/EasyStok/pull/84)).
KDS abandonado fix em [#162](https://github.com/michel-az-de/EasyStok/pull/162).

## IA (Claude integration)

**Status:** 🟡 Parcial (Q% 47% → 60% pos PR1-PR7)

`AnuncioIa`, `UsoIa` — Claude streaming + contador mensal OK. Sem precificacao
por canal.

## Admin (back-office Easystok)

**Status:** ✅ Pronto (P0-P3 + redesign UX/UI completo)

`EasyStock.Admin` projeto separado. Cobre:

- **Audit trail** (`AuditLog`, `AdminAuditLog`, `EntityAlteracao`).
- **Impersonation log** (`AdminImpersonationLog`).
- **Notas internas por tenant** (`AdminNotaTenant`).
- **Gestao de clientes P0-P3**: reset senha, forca logout, sessoes, edicao
  usuario, CRUD lojas, atividade unificada, PII unmask + audit, LGPD
  anonimizar/exportar, Cmd+K, atalhos, recentes, banner alerta.
- **Coupon engine** (`Cupom`).
- **Status page** (`EndpointHealthState`).
- **Feature flags** (`TenantFeatureFlag`).
- **Config sistema** (`ConfiguracaoSistema`).
- **Redesign UX/UI completo** com biblioteca de componentes
  ([#171](https://github.com/michel-az-de/EasyStok/pull/171),
  [#184](https://github.com/michel-az-de/EasyStok/pull/184)).
- **Dashboard v2**: KPIs priorizados, alertas em lista
  ([#122](https://github.com/michel-az-de/EasyStok/pull/122)).

Controllers admin: `AdminClientesController`, `AdminTicketsController`,
`AdminFaturasController`, `AdminCuponsController`, `AdminTenantsController`,
`AdminUsuariosTenantController`, `AdminNotificacoesController`, etc.

## Auditoria & LGPD

**Status:** ✅ Pronto

- `EntityAlteracao` — mudancas em entidade chave gravam alteracoes.
- `AuditLog` — auditoria geral.
- `AdminAuditLog`, `AdminImpersonationLog`, `AdminAcessoPiiLog` —
  auditoria de acoes admin sobre dados de cliente.
- **Self-service LGPD** anonimizar/exportar (modulo Admin Gestao Cliente).

## Onboarding & Landing

**Status:** ✅ Pronto

- Landing publica (index/precos/app/contato/sucesso) com `LeadPublico`
  anonimo + rate limit + anti-spam.
- **Onboarding wizard E2E + rate limit + Pix self-service** ([#78](https://github.com/michel-az-de/EasyStok/pull/78)).

## Webhooks & Idempotencia

**Status:** ✅ Pronto

- `WebhookRecebido`, `IdempotencyKey`, `IdempotencyMiddleware`.
- Path-prefix configuravel.
- HMAC-SHA256 signature validators por provider (Pix Efí, Stripe, MercadoPago).
- Replay protection via janela 5min.

## Infra cross-cutting

**Status:** ✅ Pronto + 🟡 alguns gaps operacionais

- **Deploy:** Fly.io para 3 apps + worker ([#167](https://github.com/michel-az-de/EasyStok/pull/167) auto-deploy,
  [#191](https://github.com/michel-az-de/EasyStok/pull/191) release_command migrations).
- **Postgres managed Render** (banco transacional vivo).
- **Logging:** Serilog estruturado, `correlationId` propagado.
- **Health checks:** `/health/live`, `/health/ready`, `/health/dispatcher`
  separados por concern.
- **Seed bootstrap** idempotente em 4 camadas
  ([#87](https://github.com/michel-az-de/EasyStok/pull/87) R6 Onda 1).
- **Architecture tests** (`EasyStock.ArchitectureTests` projeto).
- **Cobertura** medida por modulo, gate por modulo
  ([#83](https://github.com/michel-az-de/EasyStok/pull/83): Domain 73 / App 47 / Api 9).

Gaps (stability-roadmap Bloco 1-6):
- Backup PG automatizado pre-deploy (manual ok)
- Alertas Cloud Monitoring (5xx, p95, Pix pendente, jobs)
- Sentry/error tracking
- Multi-tenant leak test automatizado
- Rate limiting `/api/webhooks/pix`
- Rotacao automatica de secrets

Detalhes completos: [`.knowledge/stability-roadmap.md`](../.knowledge/stability-roadmap.md).

---

## Como manter este arquivo

Atualizar quando:
- Um modulo mudar de status (ex: planejado → pronto).
- Uma decisao arquitetural mover algo de plano para implementacao (ou diferir).
- Uma feature significativa for adicionada (entity nova, fluxo E2E novo).

Nao atualizar pra cada PR (isso e changelog). Nao duplicar `.knowledge/`
(esse arquivo e o sumario publico, nao a doc tecnica completa).
