# Current State — EasyStok (2026-05-06)

> Snapshot ao final do dia. Atualize quando deploy ou auditoria mudar.

## Numbers
- Branch: `master`
- Commits totais: **714+**
- Último commit relevante: `78fdbc1 fix(notifications): pente fino — 30 correções de segurança, correção e performance`
- Testes (domain + application + api): verdes em CI; suites principais Domain.Tests, Application.Tests (248+), Api.UnitTests (93+), IntegrationTests com Testcontainers.
- Solução: 17 projetos no `EasyStok.sln`.
- Feature parity vs Bling/Tiny/Omie: estimativa de **30–35%** (auditoria abril/2026 — sem mudança estrutural desde então; ver `audit-brutal.md`).

## Infra
- **Azure App Service ativo** (workflow `.github/workflows/deploy-azure.yml` dispara em push pra master/main). API + Web + Admin atrás de App Services separados.
- **Azure PostgreSQL Flexible** `pg-easystock` ainda é o banco transacional.
- **GCP Cloud Run** documentado em `gcp-deploy.md` como plano alternativo ($300 free credit, Cloud SQL `db-f1-micro`); **não migrado**, apenas scripts prontos.
- **Cloudflared** em `~/bin/cloudflared.exe` como backup pra túnel HTTPS público.
- **Render** NÃO está em uso (referência antiga em memória de 2026-04 — desconsiderar).

## Estado por área (atualizado pós-ondas de abril/maio)

### ✅ Pronto pra produção
- Auth completo: registro, login JWT 8h + refresh 30d, logout, reset senha, email confirmation, biometria mobile, revogação tokens.
- Multi-tenant Empresa→Loja→Usuario→Perfil/Permissão; tenant guards em todos os controllers; `ValidateEmpresaId` em body POST/PUT.
- Catálogo + Estoque: Produto com VOs (Sku, Validade, Dinheiro, Cnpj, Telefone, EmailAddress, PasswordPolicy), Categoria, Fornecedor, Lote, ItemEstoque, FEFO real na saída, idempotência em POSTs (`IdempotencyKey`), auditoria E2E LGPD/GDPR.
- Pedido com state machine + idempotência `{pedidoId}:{itemId}` em movimentação.
- Vendas/Caixa formalizado (movimentos + fechamento).
- **PedidoFornecedor agora persiste itens** (`PedidoFornecedorItem` entity adicionada — P0 RESOLVIDO).
- **Webhook Pix valida valor pago** vs cobrança (R$0,01 vuln RESOLVIDO em commit `37fb7d9`).
- **DiagnosticoController `[Authorize(Policy="Admin")]`** (21 endpoints AllowAnonymous RESOLVIDO em `c5d2ad6`).
- Notificações multi-canal (PR1–PR7): Email/SMS/WhatsApp/InApp via Outbox + Worker dispatcher + painel Admin + LGPD self-service + métricas OTel.
- Helpdesk: AdminTicket reformado (TicketAnexo, TicketHistorico, AdminTicketTecnicoMeta, SlaConfiguracao), SlaMonitorService, 9 eventos de notificação globais.
- EasyStock.Admin completo: design system marca, audit trail, coupon engine, status page, gestão admins, feature flags, config sistema, gestão cliente P0–P3 (reset senha, força logout, sessões, edição usuário, CRUD lojas, atividade unificada, PII unmask + audit, LGPD anonimizar/exportar, Cmd+K, atalhos, recentes, notas internas, banner alerta).
- Mobile MAUI (`EasyStok.Mobile`): F0 setup → F1 estrutura → F2 auth E2E (JWT+refresh+biometria) → F2b/c multi-tenant pickers + permissões → F3 produção SQLite + REST → F4 mutations otimistas (outbox SQLite) → F4b SyncEngine → F4c popup captura foto/peso/validade.
- PWA Casa da Babá: scanner QR/Code128, conferência rápida, etiquetas térmicas WTP05, BT plugin nativo, LiveUpdater, offline-first.

### 🟡 Parcial (usável com gap)
- Analytics/Dashboard (números OK, drill-down limitado).
- IA (Claude streaming + contador mensal OK; precificação por canal não existe).
- Subscription/Billing (Trial 14d + Pix Efí + gate middleware OK; sem rotação automática de planos quando muda).

### ❌ Não-prod
- NF-e/NFC-e/NFS-e/MDF-e: **zero**.
- Marketplaces (Mercado Livre/Shopee/Magalu): **zero**.
- Multi-empresa por usuário: **parcial**.
- Variantes/grades de produto (cor/tamanho): **estrutura existe** mas UI não exposta.

## Vulnerabilidades conhecidas
- **Webhook Pix replay window**: Efí pode mandar 2x simultâneo dentro de 5 min — race em `ProcessarPagamentoAsync` ainda possível. Tracked em `stability-roadmap.md` Bloco 5.
- **`SubscriptionGateMiddleware` sem cache**: bate no DB toda request autenticada.

## O que falta pro MVP-pago (atualizado)
1. Deploy GCP estável (se decidir migrar — Azure paga hoje).
2. Rate limiting em `/api/webhooks/pix` (login/register cobertos desde B-015).
3. NF-e mínimo via emissor third-party (Focus NFe/eNotas).
4. Onboarding cliente externo testado E2E (signup → trial → upgrade).
5. CI bloqueando merge com teste vermelho.

## Commits recentes relevantes (últimos 10)
```
78fdbc1 fix(notifications): pente fino — 30 correcoes de seguranca, correcao e performance
76d31af feat(helpdesk): reformar pagina Tickets (Index + Detail) — UI multi-nivel + SLA + LGPD
44478f6 test(helpdesk): factories de Domain
e66e8c4 feat(helpdesk): SlaMonitorService — varre tickets e dispara SlaProximoVencer/SlaViolado
a0e556f feat(helpdesk): templates + rotinas globais para os 9 eventos novos de notificacao
3a74550 feat(helpdesk): API controllers (admin tickets reformados + empresa preview LGPD + sla)
46a0c07 fix(seed): [NotMapped] em IsSeedData — elimina coluna de todo SQL gerado pelo EF
9909cc1 feat(helpdesk): services para abrir/responder/encaminhar/anexar/bug-fix/preview cliente
aa542ea feat(helpdesk): migration AddHelpdeskCore + backfill SLA
d754f84 fix(seed): solucao definitiva — schema bootstrap idempotente em SQL
```
