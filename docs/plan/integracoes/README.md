# Motor de integrações: requisitos (pagamento + logística)

Issue: #1203 · Levantamento: 2026-09-30 · Status: DECIDIDO em 2026-09-30 (seção 6)

Provedores: Mercado Pago, PayPal, Pix direto (PSP), 99 Entrega, Lalamove.

> Taxas, validade de token e cidades atendidas marcadas como **não confirmado** vieram de fontes
> secundárias. Conferir na fonte oficial antes de codar ou contratar.

## 1. Veredito

| Provedor | API pública | Bloqueio para contratar | Esforço |
|---|---|---|---|
| Mercado Pago | Sim | Nenhum (self-service) | Médio (stub existe) |
| Pix direto (Efí) | Sim | Nenhum (Efí já em uso) | Baixo (já funciona) |
| Lalamove | Sim | Wallet pré-pago em produção | Médio |
| PayPal | Sim | Multiparty exige aprovação comercial | Médio |
| 99 Entrega | **NÃO CONFIRMADA** | Contato comercial | Desconhecido |

## 2. Estado atual do código

| Peça | Arquivo | Estado |
|---|---|---|
| Porta de pagamento | `EasyStock.Application/Ports/Output/Pagamentos/IPagamentoGateway.cs` | Existe |
| Roteador + regras | `EasyStock.Infra.Async/Pagamentos/PagamentoGatewayRouter.cs`, `GatewayRoutingRule` | Existe |
| Orquestrador + failover | `EasyStock.Infra.Async/Pagamentos/PagamentoOrchestrator.cs` | Existe |
| Saúde do gateway | `NoopGatewayHealthStore` | Noop |
| Efí Pix | `EfiPixGatewayAdapter`, `EfiPixWebhookProcessor`, conciliação | Pronto |
| Mercado Pago | `MercadoPagoGatewayAdapter.cs:59`, `MercadoPagoWebhookProcessor` (S32) | Processor do webhook existe desde a S32 (este levantamento é anterior); chave ainda global (`MercadoPagoOptions`) |
| Credencial por loja | `credencial_integracao`, `IntegrationCredentialResolver` (AES-256-GCM com KEK) | Existe; RLS, chave mestra obrigatória, teste e vigia na F16 (#1246). Consumidores reais ainda leem a chave global |
| Stripe | `StripeGatewayAdapter.cs:67` | Stub |
| Webhook genérico | `WebhookGatewayController.cs` | Sem processor devolve 500 |
| Entrega | `CalcularFreteUseCase`, `FreteZona`, `JanelaEntrega` | Só frete interno; sem provedor externo |

### Lacunas

1. ~~Credencial por tenant inexistente.~~ Corrigido em 2026-10-01: a tabela e o resolver já existiam
   ociosos; a F16 (#1246) ligou RLS, KEK obrigatória, API de chaves, teste e vigia. Pagamento e
   geocoding ainda leem a chave global (decisão registrada na #1246).
2. ~~Processor de webhook só para Efí.~~ O Mercado Pago tem `MercadoPagoWebhookProcessor` desde a S32.
3. Sem retry/circuit breaker (nenhum Polly/`AddResilience`).
4. Sem porta de logística.
5. Conciliação por job de provedor, não genérica.
6. Pagamento confirmado não avança o status do pedido.
7. `MercadoPago__*` ausente em `render.yaml`, `fly.toml`, `docker-compose.azure.yml`.

## 3. Arquitetura proposta do motor

```
                 ┌──────────── Núcleo comum ────────────┐
                 │ TenantIntegracao (credencial cifrada) │
                 │ Webhook: assinatura → dedupe → fila   │
                 │ Idempotência · Outbox · Resiliência   │
                 │ Reconciliação genérica (polling)      │
                 └───────────┬──────────────┬────────────┘
                             │              │
              IPagamentoGateway        ILogisticaGateway (NOVA)
              (existe, estender)       Cotar · Criar · Consultar · Cancelar
              ├ EfiPix  ✔              ├ Lalamove
              ├ MercadoPago            ├ NoventaENove (se houver API)
              ├ PayPal                 └ Interno (frete por raio, existente)
              └ Manual ✔
```

Princípios:
- Reaproveitar porta, roteador e orquestrador de pagamento; a porta de logística copia o molde.
- Webhook nunca é fonte de verdade: validar assinatura, deduplicar, **reconsultar o recurso** no provedor.
- Dinheiro cai na conta do tenant (OAuth/credencial própria), nunca na da FMA, para evitar subcredenciamento.
- Segredo (token, `.p12`, secret) cifrado fora do banco em claro, com data de expiração e rotação.
- Módulos ligados por `TenantFeatureFlag` (ADR-0048).

## 4. Requisitos por provedor

### 4.1 Mercado Pago
- **Contratação:** app no painel Developers. Cada tenant conecta a própria conta via **OAuth marketplace**; FMA cobra comissão por `marketplace_fee`.
- **Auth:** Bearer; guardar `access_token`, `refresh_token`, `public_key` por tenant. Expiração ~180 dias (**não confirmado**), exige job de refresh.
- **Endpoints:** `POST /v1/orders` (Pix, cartão, boleto), `POST /checkout/preferences` (Checkout Pro), `POST /oauth/token`.
- **Webhook:** tópicos `order`/`payment`. Header `x-signature: ts=..,v1=..`; manifest `id:{data.id};request-id:{x-request-id};ts:{ts};`, HMAC-SHA256 com a secret do app. Normalizar `data.id` para minúsculas.
- **Sandbox:** credenciais e usuários de teste.
- **Taxas (não confirmado):** Pix 0,99%; cartão 4,99% na hora, 3,99% em 30 dias.
- **Fontes:** mercadopago.com.br/developers/pt/docs/checkout-api-orders/notifications · .../checkout-pro/how-tos/integrate-marketplace

### 4.2 PayPal
- **Contratação:** conta Business (CNPJ). Operar em nome dos tenants exige **Multiparty/Partner**, com aprovação comercial.
- **Auth:** OAuth2 client_credentials; em nome do seller, `PayPal-Auth-Assertion` + BN code.
- **Endpoints:** `POST /v2/checkout/orders`, `/{id}/capture`, `POST /v2/customer/partner-referrals`.
- **Webhook:** `PAYMENT.CAPTURE.COMPLETED`, `MERCHANT.ONBOARDING.COMPLETED`; validar via `POST /v1/notifications/verify-webhook-signature`.
- **Taxa (oficial, tabela jul/2025):** 4,79% + R$0,60 por venda nacional.
- **Risco:** Pix via PayPal BR não confirmado; taxa alta; conflita com a decisão de 22/09 (MP gateway único).
- **Fontes:** paypal.com/br/business/paypal-business-fees · developer.paypal.com/docs/multiparty/

### 4.3 Pix direto (API Pix BACEN via PSP)
- **Contratação:** conta PJ no PSP do tenant (Efí, Inter, Itaú, Sicredi), chave Pix e API habilitada.
- **Auth:** OAuth2 client_credentials sobre **mTLS** (certificado `.p12` por tenant, validade anual).
- **Endpoints:** `/v2/cob`, `/v2/cobv/{txid}` (vencimento, juros, multa), `/v2/pix`, `PUT /v2/webhook/{chave}`, devolução `/v2/pix/{e2eid}/devolucao/{id}`.
- **Pix Automático** (desde jun/2025): `/rec`, `/cobr`, `/locrec`. Útil para `CobrancaAssinatura`.
- **Webhook:** PSP chama com mTLS; na Efí, variante `?hmac=` evita mTLS no ingress (Fly.io).
- **Taxas:** por contrato, **não confirmado**.

### 4.4 99 Entrega
- **API pública de despacho: NÃO CONFIRMADA.** O que existe: 99Food Open Platform (pedidos de restaurante, Open Delivery) e API 99 Empresas (corridas corporativas).
- **Caminho:** contato comercial (gerente 99 Empresas / 99app.com/ajuda/empresa/como-solicitar/) ou agregador (Frenet) como via indireta.
- **Ação:** Felipe abre contato comercial antes de qualquer código.

### 4.5 Lalamove (API v3)
- **Contratação:** Partner Portal; sandbox self-service; produção com wallet pré-pago.
- **Auth:** `Authorization: hmac {KEY}:{ts}:{SIG}`, SIG = HMAC-SHA256(secret, `{ts}\r\n{METHOD}\r\n{path}\r\n\r\n{body}`); header `Market: BR`.
- **Endpoints:** `POST /v3/quotations` (preço garantido por prazo), `POST /v3/orders`, `GET/DELETE /v3/orders/{id}`, `GET /v3/cities`, `PATCH /v3/webhook`.
- **Webhook:** sem assinatura documentada (**não confirmado**). Mitigar com token secreto na URL e reconsulta de `GET /orders/{id}`.
- **Cidades BR e rate limit:** consultar `GET /cities` no sandbox (**não confirmado**).

## 5. Fatiamento proposto (issues filhas)

| # | Fatia | Tier | Depende de |
|---|---|---|---|
| F1 | `TenantIntegracao`: credencial cifrada por tenant + expiração | ALTO (migração) | Decisão D2 |
| F2 | Núcleo de webhook: despacho por provedor, dedupe, reconsulta | ALTO | F1 |
| F3 | Resiliência (Polly) + saúde do gateway real | ALTO | nenhuma |
| F4 | Mercado Pago completo: OAuth, `/v1/orders`, processor, refresh | ALTO | F1, F2 |
| F5 | Pagamento confirmado avança o pedido (outbox) | ALTO | F4 |
| F6 | Porta `ILogisticaGateway` + adapter Interno | ALTO | nenhuma |
| F7 | Lalamove adapter | ALTO | F1, F6 |
| F8 | ~~PayPal adapter~~ fora (D1) | n/a | n/a |
| F9 | 99 Entrega adapter | ALTO | contato comercial |
| F10 | Pix Automático para assinaturas | ALTO | F1 |

## 6. Decisões (Felipe, 2026-09-30)

| # | Decisão | Escolha | Efeito |
|---|---|---|---|
| D1 | PayPal | **Não por ora** | F8 fora do escopo; reabrir só com demanda |
| D2 | Quem recebe | **Conta do tenant** | OAuth MP por tenant + `marketplace_fee`; sem subcredenciamento |
| D3 | Pix de pedido | **PSP próprio do tenant** | Pix sai do MP e vai para o PSP do tenant (Efí primeiro). Revisa em parte a decisão A de 22/09: MP fica para cartão/boleto. F1 precisa guardar certificado `.p12` com validade e rotação |
| D4 | Ordem | **Pagamento primeiro** | F1 → F2 → F3 → F4 → F5; logística (F6, F7) depois |

## 7. Ações comerciais (dependem do Felipe, não do agente)

1. Mercado Pago: criar app marketplace no painel Developers (conta FMA).
2. ~~PayPal~~: fora do escopo (D1).
3. Lalamove: cadastro no Partner Portal, chave de sandbox.
4. 99: contato com gerente 99 Empresas perguntando por API de despacho de entregas.
5. Efí: nada novo (já contratado).
