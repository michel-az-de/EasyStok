# Product: EasyStok

**Last updated:** 2026-06-27
**Method:** codebase scan + conversation

## Product Identity
- **One-liner:** A small food/retail business registers its products and stock, rings up sales at the counter (Caixa/POS), takes online orders through a public menu storefront, issues Brazilian fiscal receipts (NF-e/NFC-e), prints nutritional labels, and runs its finances — all from one web panel, an Android app, and a PWA.
- **Category:** b2b-saas (vertical SaaS for foodservice / small retail)
- **Product type:** B2B — multi-tenant. Group hierarchy and account-level (Empresa) tracking apply. A secondary B2C surface exists (the public storefront where end-consumers place orders), but the paying customer and primary user is the lojista (shop owner/operator).
- **Collaboration:** multiplayer — multiple users per Empresa, with roles/permissions (Perfil/PerfilPermissao), and a transversal SuperAdmin back-office.

## Business Model
- **Monetization:** freemium — free trial (`AssinaturaEmpresa.TrialFim` / `AtivarTrial`) converting to paid monthly plans. Lifecycle: Ativa → Suspensa → Cancelada → Expirada (`StatusAssinatura`); trial expiry transitions to `Expirada` and is gated by `SubscriptionGate`.
- **Pricing tiers:** Defined dynamically as rows in the `planos` table (`Plano` entity), not hardcoded — so exact named tiers aren't enumerable from code. Each plan carries `PrecoMensal` and quota limits: `LimiteLojas`, `LimiteUsuarios`, `LimiteProdutos`, `LimiteGeracoesIaMensais` (a value of `-1` / `Plano.SemLimite` means unlimited). Discount coupons exist (`Cupom`, snapshotted onto the subscription).
- **Billing integration:** Two distinct flows. (1) **Subscription billing** is largely internal — `Fatura`, `FaturaPagamento`, `CobrancaAssinatura`, `FaturaContador`. (2) **Storefront customer payments** run through a multi-gateway orchestration layer: **Mercado Pago** and **Stripe** adapters (`StripeGatewayAdapter`, `MercadoPagoGatewayAdapter`), with `GatewayRoutingRule`, `GatewayHealthSnapshot`, `PaymentAttempt`, signature validators, and Pix + gateway webhooks (`WebhookPixController`, `WebhookGatewayController`).

## Tech Stack
- **Primary language:** C# (.NET 9)
- **Framework:** ASP.NET Core. `EasyStock.Api` = Web API + PWA host (`wwwroot/pwa/`); `EasyStock.Web` = MVC + Alpine.js + Tailwind (operator panel, a pure HTTP BFF with no project reference to Domain/Application); `EasyStock.Admin` = Razor Pages back-office; `EasyStok.Mobile` = .NET MAUI (Android).
- **Database:** PostgreSQL (Azure) via EF Core 9, with PostgreSQL **Row-Level Security (RLS)** plus an EF `HasQueryFilter` layer for tenant isolation (`IsSuperAdmin || EmpresaId == tenant`). `EasyStock.Infra.Postgre` is the migrations assembly. (MongoDB was evaluated and rejected as transactional store — ADR-0001.)
- **Background jobs:** `EasyStock.Worker` — Outbox-pattern dispatcher draining notification outbox to Email/SMS/WhatsApp/InApp/Push; plus hosted/cron jobs (e.g., subscription expiry/dunning).
- **HTTP client patterns:** `HttpClient` / typed clients. `EasyStock.Web` talks to the API purely over HTTP (BFF). External integrations (Mercado Pago, Stripe, FocusNFe fiscal) use `HttpClient` adapters in `EasyStock.Infra.Integrations` / `EasyStock.Infra.Async`.
- **Module organization:** Clean Architecture (strict, enforced by ArchitectureTests). Core: `EasyStock.Domain`, `EasyStock.Application` (use cases + ports). Infra: `Infra.Postgre`, `Infra.Async`, `Infra.Notifications`, `Infra.Integrations`. UI projects are separate. Contracts in `EasyStock.Contracts`.

## Value Mapping

### Primary Value Action
**Ring up a sale at the cash register (Caixa/POS)** — record a confirmed payment and the corresponding stock movement within a cash session. This is the real production workload of the Casa da Babá tenant and the transactional heart of the product. If this drops to zero, the product has failed and the customer "goes back to the spreadsheet."

### Core Features (directly deliver value)
1. **Caixa / POS** — cash session (`SessaoCaixa`: aberta → em_conferencia → fechada), `MovimentoCaixa`, multiple/partial payments per order, audited reversals, conciliated closing with hash + receipt PDF. Directly captures revenue.
2. **Estoque / Inventory movements** — `ItemEstoque`, `Lote`, `MovimentacaoEstoque`. Keeping stock accurate is why the operator trusts the till and the menu.
3. **Pedidos (Orders)** — `Pedido` lifecycle, payments, status flow. The unit of fulfillment.
4. **Storefront / online menu** — public `cardápio`/vitrine, checkout, frete, agendamento, avaliação. The online sales channel feeding orders.
5. **Fiscal (NF-e / NFC-e)** — `NotasFiscais`, FocusNFe integration, fiscal config. Compliance that makes the sale "real" in Brazil.
6. **Rotulagem / Labels** — `EtiquetaTemplate`, nutritional labeling and label printing. A differentiating value for food producers (ADR-0011/0021).

### Supporting Features (enable core actions)
1. **Catálogo / Product registration** — `Produto`, variations, packaging, composition. Prerequisite for selling.
2. **Financeiro** — accounts payable/receivable, cost centers, financial categories (feature-flagged per tenant).
3. **Notificações** — Outbox → Email/SMS/WhatsApp/InApp/Push, keeping users informed.
4. **Auth & Onboarding** — JWT (8h) + refresh (30d), mobile biometrics, store-creation onboarding gate.
5. **Assinatura / Billing** — plans, trial, coupons, invoices, subscription gating.
6. **Admin / Back-office** — tenant management, plans, faturas, SLA, helpdesk tickets, impersonation, auditing, AI-assisted listings.

## Entity Model

### Users
- **ID format:** `Guid` (`Usuario.Id`).
- **Roles:** Role/permission via `Perfil` + `PerfilPermissao` (scoped per Empresa through `UsuarioPerfil`); helpdesk attendant tiers `NivelAtendimento` (N1–N4); a transversal **SuperAdmin** for the back-office. Storefront end-consumers are a separate, lighter actor (guest/cliente checkout).
- **Multi-account:** yes — a user can belong to multiple Empresas via `UsuarioEmpresa`.

### Accounts
- **ID format:** `Guid` (`Empresa.Id`). This is the tenant boundary for RLS.
- **Hierarchy:** nested — `Empresa` (account/tenant) contains one or more `Loja` (store/location). Stock and sales are recorded at the `Loja` level.

## Group Hierarchy

```
Empresa (account / tenant)
└── Loja (store / location)
```

| Group Type | Parent | Where Actions Happen |
|------------|--------|---------------------|
| Empresa | — (top) | Billing, subscription, plan limits, admin/governance, cross-store reporting |
| Loja | Empresa | Cash sessions, sales, stock movements, orders, fiscal receipts, labels |

**Default event level:** Loja (the most specific level — where the primary value action happens).
**Admin actions at:** Empresa (subscription, plan, user management, governance).

## Current State
- **Existing tracking:** No third-party **product-analytics SDK** was identified in this product-modeling scan (to be confirmed by the audit phase). Present in-house: `AnalyticsController` / `InteligenciaController` (internal BI/insights endpoints) and heavy **compliance/forensic audit logging** (`AuditLog`, `EntityAlteracao`, `AdminAuditLog`, `AdminAcessoPiiLog`, impersonation logs) — these are governance trails, not product engagement analytics. There is also operational observability (OTel + 5xx failure counters, ADR-0036) and a `/health` endpoint exposing the deployed commit.
- **Documentation:** yes — extensive: `.knowledge/` (single source of truth), `docs/adr/` (ADRs), `docs/plan/`, incident logs.
- **Known issues:** triple-frontend duplication (Web panel + PWA + MAUI) raises instrumentation surface; manual/cron VM deploys cause deploy lag that has historically produced false "not fixed" QA reports; no unified product-engagement telemetry today.

## Integration Targets
| Destination | Purpose | Priority |
|-------------|---------|----------|
| Accoil | Product-engagement scoring / account health | 1 |
| Segment (CDP) | Fan-out hub to route events to multiple downstream destinations | 2 |
| PostHog / Amplitude / Mixpanel | Product analytics — funnels, retention, per-event property analysis | 2 |

**Destination constraint to carry into design:** **Accoil stores event names only — no event properties.** Because Accoil is a target, the event-naming strategy in the design phase must encode meaning into the **event name itself** (semantic, human-readable names) rather than relying on properties to disambiguate. Properties can still be sent for the richer destinations (Segment fan-out, PostHog/Amplitude/Mixpanel), but every event must be analytically meaningful by name alone.

## Codebase Observations
- **Feature areas inferred:** From ~90 API controllers — `Admin*` (tenants, faturas, planos, SLA, tickets, auditoria, diagnóstico, IA), `Storefront/*` (menu público, checkout, frete, agendamento, avaliação, pedidos cliente), `Caixa`, `Pedidos`, `Produto`/`ItemEstoque`/`Lotes`/`Movimentacao`, `NotasFiscais`/`ConfiguracaoFiscal`, `Financeiro`/`ContasAPagar`/`ContasAReceber`/`CentrosCusto`, `AssinaturaCliente`/`Plano`/`Faturas`, `EtiquetaTemplates`, `Notificacao`/`PwaPush`, `Auth`/`Onboarding`/`Usuario`, and gateway/Pix/FocusNFe webhooks.
- **Entity model inferred:** Multi-tenant `Empresa → Loja → Usuario (via UsuarioEmpresa) → Perfil/Permissao`, RLS-isolated. Catalog: `Produto` (+ variação, embalagem, composição, característica), `ItemEstoque`, `Lote`, `MovimentacaoEstoque`. Sales/cash: `Pedido`, `SessaoCaixa`, `MovimentoCaixa`, `PedidoPagamento`. Billing: `AssinaturaEmpresa`, `Plano`, `Cupom`, `Fatura`. Payments: `PaymentAttempt`, `Gateway*` (Mercado Pago + Stripe). Mobile-sync mirror entities under `Entities/Mobile/` (Order, Product, Client, Batch, CashEntry, MobileDevice).
