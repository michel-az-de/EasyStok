# Tech Debt — EasyStok

> Atualizar quando resolver. Ordenado por impacto. Itens fechados ficam ao final como histórico.

## P0 — Bloqueia produção real

> Os 3 P0 antigos (PedidoFornecedor.Itens, Webhook Pix valor, DiagnosticoController) estão RESOLVIDOS — ver "Resolvidos" no fim deste arquivo. Rate limit em `/auth/*` (B-015) também resolvido — falta cobrir `/api/webhooks/pix`.

1. **Sem NF-e/NFC-e**
   - Cliente brasileiro real exigiu desde a primeira conversa de venda. Bling/Tiny tem de fábrica.
   - Decisão pendente: integrar emissor third-party (Focus NFe / eNotas) ou construir interno.

2. **Rate limiting parcial em endpoints públicos**
   - `/api/auth/login`, `/api/auth/register`, `/api/auth/refresh`, `/api/auth/forgot-password`, `/api/auth/reset-password` cobertos pela policy `auth` (10/min/IP, fixed-window).
   - Falta cobrir `/api/webhooks/pix` (DOS + replay attack ainda em aberto).

3. **CI não bloqueia merge com teste vermelho**
   - Workflows `deploy-azure.yml` deploy direto, sem gate de qualidade prévio.
   - Fix: workflow separado `ci.yml` com `dotnet test` requerido em PR.

## P1 — Confiabilidade

4. **Compras (recebimento) sem teste de integração**
   - Entity persiste agora, mas fluxo `ItemEstoque` não tem cobertura E2E.

5. **Sem teste E2E pro fluxo Pedido→Venda→Caixa**
   - Cobertura unit existe; bugs de integração escapam.

6. **`Infra.MongoDb` é parcial e divergente do Postgre**
   - ADR 0001 (`docs/adr/0001-mongo-discarded.md`) descartou Mongo como provedor transacional.
   - Projetos `EasyStock.Infra.MongoDb` e `EasyStock.Infra.MongoDb.IntegrationTests` ainda fisicamente no repo.
   - Decisão pendente: deletar fisicamente os projetos ou manter como dead-ish code.

7. **Webhook Pix idempotência em duplo-fire**
   - Efí pode mandar 2x dentro de 5 min — race em `ProcessarPagamentoAsync`.
   - Fix: lock distribuído (`SELECT ... FOR UPDATE` em `cobrancas` por txid) ou idempotency key na request.

## P2 — Qualidade

8. **PWA mobile com `sync.js` monolítico**
   - `EasyStock.Api/wwwroot/pwa/sync.js` ~1000 linhas, sem testes.
   - Não migrar agora, mas isolar mudanças.

9. **Falta migration formalizada para `xmin` em entidades novas**
   - `xmin` é system column, mas a config Fluent API precisa ser repetida — fácil esquecer ao adicionar entidade.

10. **`appsettings.Production.json` com placeholders não-óbvios**
    - `Efi:ClientId`, `Stripe:Webhook`, etc. Onboarding novo dev confuso.

11. **`SubscriptionGateMiddleware` não tem cache**
    - Bate no DB toda request autenticada. Latência cumulativa em escala.

## P3 — Cosmético / Future

12. Sem feature flags maduras (`Microsoft.FeatureManagement`).
13. Sem health-check estruturado completo (`/health/live`, `/health/ready`) — middleware existe parcial.
14. Status de pedido como `string` — adicionar enum-string converter pra evitar typos em transição.
15. Logs sem `correlationId` propagado consistentemente em todas requests.

---

## Resolvidos (histórico — não regredir)

- [x] **`PedidoFornecedor.Itens [NotMapped]`** — 2026-04-30. Entity `PedidoFornecedorItem` criada com migration `20260502120000_AddPedidoFornecedorItemTable`. Persiste e dá entrada de estoque correta no recebimento.
- [x] **Webhook Pix sem validação de valor** — 2026-05-01 (`37fb7d9 fix(billing): webhook Pix valida valor pago vs cobrança`). Vuln R$0,01 fechada.
- [x] **`DiagnosticoController` exposto com 21 `[AllowAnonymous]`** — 2026-04-30 (`c5d2ad6`). Class agora `[Authorize(Policy="Admin")]`.
- [x] **`Math.Ceiling` em qty fracionária descontava errado** — 2026-04. Saldo negativo silencioso eliminado, usa decimal exato.
- [x] **`pedido.Itens` vazio por falta de `Include`** — 2026-04. `GetByIdWithDetailsAsync` carrega aggregate completo.
- [x] **Pedido → estoque com idempotência** — 2026-04 (`340aff0`). Chave `{pedidoId}:{itemId}`.
- [x] **xmin RowVersion em entidades-chave** — 2026-04. Produto, Pedido, ItemEstoque, AssinaturaEmpresa.
- [x] **Mongo descartado como provedor transacional** — 2026-05-01 (`820843c`). ADR 0001 formal.
- [x] **B-015 — Rate limit em `/api/auth/login` e `/api/auth/register`** — 2026-05-07. Policy `auth` (fixed-window 10/min particionada por IP, `QueueLimit=0`) ativa em login/register/refresh/forgot-password/reset-password. Resposta 429 com `Retry-After` + envelope `RATE_LIMIT_EXCEEDED`. Coberto por `EasyStock.Api.IntegrationTests/AuthRateLimitTests.cs`. `/api/webhooks/pix` ainda fora — virou item residual no P0 #2.
