# Stability Roadmap — EasyStock

> **Para o próximo Claude:** este arquivo lista o que falta pra **estabilizar** as features que já existem (não features novas). Sempre que algo aqui for resolvido, **marque com `[x]` e adicione data + commit hash**. Sempre que descobrir um novo gap operacional, **adicione na seção apropriada** com `[ ]`. Não delete itens resolvidos — vire-os check pra preservar histórico.
>
> Princípio: **código está em ~40% parity, operação em ~15%**. Estabilizar = fechar deploy, CI, backup, alerta, testes de integração.

Última atualização: 2026-05-07

---

## Bloco 1 — Confiabilidade (P0)

- [ ] **Deploy estável GCP**: Cloud Run + Cloud SQL com 1 prod + 1 staging. Dockerfiles prontos (`Dockerfile.cloudrun.{api,web,admin}`). Aguarda Project ID. Ver `.knowledge/gcp-deploy.md`.
- [~] **Backup Postgres + restore testado** — 2026-05-07 (Onda 1 R6). Runbook manual escrito (`docs/runbook/pg-restore-pitr.md`). **Pendente**: exercicio mensal de restore + backup pre-deploy automatizado via CI (Onda 2, depende de Service Principal Azure).
- [~] **CI rodando testes em cada PR** — 2026-05-07 (Onda 1 R6). `.github/workflows/ci.yml` criado com unit tests + SeedFlow integration tests. **Pendente**: estender pra rodar todos os 474 testes + branch protection no master exigindo `seed-flow-integration-tests`.
- [x] **R6 — Seed fragil mitigado (Onda 1)** — 2026-05-07. Pipeline fail-loud, advisory lock no startup, double opt-in `/seed/*` em prod, fail-fast `SuperAdminSeed`, skip `SeedData` fora de Development, testes integracao DB-do-zero + idempotencia 3x. Ver `recent-evolution.md` snapshot 2026-05-07.
- [ ] **Health checks reais** (`/health/live`, `/health/ready`) usados pelo Cloud Run pra restart automático. Middleware existe parcial — conferir e completar. (Parcial: 2026-05-07 — `/health/api` (PG+Redis+Config) e `/health/dispatcher` (heartbeat dos 3 loops Hosted) separados pra evitar cascata API↔Outbox quando `Notifications:Hosting:Mode=Hosted`. Falta plugar no Cloud Run/App Service e cobrir Worker via Kestrel mínimo.) Pipeline Azure ja usa `/health/ready` com retry (R6 Onda 1).
- [ ] **Compras → estoque ponta-a-ponta testado**: `PedidoFornecedorItem` foi criada (commit recente) mas fluxo de recebimento → entrada de movimentação **sem teste de integração**.

### R6 Onda 2 e 3 (proximos)
- [ ] **R6 Onda 2 (P1)**: migration formal `IsSeedData` (remove `[NotMapped]`), dupla checagem em `UpsertEmpresaAsync` (CNPJ colision proof), dry-run mode, backup pre-deploy automatizado, alertas em prod quando seed roda.
- [ ] **R6 Onda 3 (P2)**: `SeedFingerprint` table, endpoint rollback `/seed/rollback/{runId}`, cleanup script CLI, sentinela `Empresa.Source` enum.

## Bloco 2 — Observabilidade (P0/P1)

- [ ] **Logs estruturados centralizados**: Serilog → Cloud Logging automático no Cloud Run. Garantir `correlationId` propagado em toda request (middleware existe, conferir).
- [ ] **Alertas mínimos no Cloud Monitoring**:
  - 5xx > 1% por 5 min
  - Latência p95 > 2s
  - Cobranças Pix com `Status=Pendente` > 24h
  - Job de assinatura falhou (sem execução em 25h)
- [ ] **Status Page com métricas reais**: já existe estrutura no Admin (commit `1e97128`), falta plugar dados.
- [ ] **Sentry/error tracking** (ou equivalente): exception sem stack + contexto = debug por adivinhação.

## Bloco 3 — Cobertura de testes onde dói (P1)

Hoje 474 unit/component, mas **integration gaps** em:

- [ ] Compras (`PedidoFornecedor`) recebimento → entrada de estoque
- [ ] Pedido → Venda → Caixa (fluxo financeiro completo)
- [ ] Webhook Pix E2E: payload real assinado, valor parcial, replay, sobrepagamento
- [ ] Subscription lifecycle: trial → expira → suspende → paga → reativa
- [ ] **Multi-tenant leak test** automatizado: 2 empresas, garantir que A nunca lê B em **nenhum** endpoint

Custo estimado: 30–50 testes. Pega ~80% de bugs futuros.

## Bloco 4 — Segurança operacional (P1)

- [ ] **Rotação de secrets**: JWT key, Efí keys, SMTP. Mover de `appsettings` pra Secret Manager + processo documentado.
- [ ] **Rate limiting `/api/webhooks/pix`** (login/register/refresh/forgot/reset cobertos pela policy `auth` desde B-015 — ver "Itens resolvidos").
- [ ] **Auditoria de `[AllowAnonymous]` restantes**: DiagnosticoController OK; conferir Mobile e outros Webhooks.
- [ ] **HTTPS-only + HSTS** no Cloud Run.
- [ ] **CSP / anti-XSS** no Web e Admin (Razor escapa por padrão, mas inline scripts são comuns).

## Bloco 5 — Tech debt que vira incidente (P1)

- [ ] **Decisão `Infra.MongoDb`**: mata ou alinha. Hoje código parcial divergente do Postgre, com fallback exótico em transação que vai dar bug em prod.
- [x] **Cache no `SubscriptionGateMiddleware`** — 2026-05-07. `ISubscriptionStatusCache` (IMemoryCache, TTL 60s, `Cache:SubscriptionStatusDuration` configurável) + `AssinaturaCacheInvalidationInterceptor` (EF SaveChangesInterceptor) invalidando o tenant após qualquer mutação em `AssinaturaEmpresa`. Snapshot imutável `SubscriptionStatusSnapshot` evita cachear entidade rastreada cross-request.
- [ ] **Auditoria de `Math.Round`/`Quantidade`** em todas movimentações: transferência, devolução, ajuste, inventário (já fixado em saída de pedido).
- [ ] **Idempotência de webhook duplicado**: Efí pode mandar 2x simultâneo dentro da janela de replay (5 min) — race condition possível em `ProcessarPagamentoAsync`.

## Bloco 6 — Operacional (P2)

- [~] **Runbook básico** (`docs/runbook/`):
  - [x] Restaurar backup PG (`docs/runbook/pg-restore-pitr.md` — Onda 1 R6)
  - [ ] Suspender tenant
  - [ ] Reprocessar webhook Pix perdido
  - [ ] Rollback Azure App Service deployment slot
  - [ ] Rotacionar secret
- [ ] **Migrations idempotentes auditadas** (lição do `AddAdminModule` duplicando tabela).
- [x] **Seed de demo** confiável pra onboarding e screenshot — 2026-05-07 (Onda 1 R6). Bloqueado em prod sem double opt-in, idempotencia testada 3x consecutivas em CI.
- [ ] **PWA versioning + cache busting**: hoje `sw.js` controla, update fica preso pra usuários (Casa da Babá já viu). Estratégia formal de update.

---

## Cronograma sugerido (4 semanas focadas)

| Sem. | Foco | Por quê |
|---|---|---|
| 1 | Bloco 1 #1–4 | Sem deploy + CI + backup, qualquer outra coisa é teatro |
| 2 | Bloco 2 + Bloco 3 #1, #5 | Observabilidade + testes nos pontos cegos |
| 3 | Bloco 4 #2, #3 + Bloco 3 #2, #3, #4 | Segurança operacional + integração crítica |
| 4 | Bloco 5 #1, #2 + Bloco 6 #1 | Limpeza e runbook |

---

## Itens resolvidos (histórico)

> Mover daqui pra cima ao concluir, com data e commit. Mantém memória institucional.

- [x] **Webhook Pix valida valor pago vs cobrança** — 2026-05-01 (commit `37fb7d9`). Fechou último P0 da auditoria de abril.
- [x] **`PedidoFornecedor.Itens [NotMapped]`** — 2026-04-30. Entity `PedidoFornecedorItem` persiste itens (migration `20260502120000_AddPedidoFornecedorItemTable`).
- [x] **`DiagnosticoController` exposto** — 2026-04-30 (`c5d2ad6`). Class agora `[Authorize(Policy="Admin")]`.
- [x] **Pedido → estoque com idempotência por `{pedidoId}:{itemId}`** — 2026-04 (commit `340aff0`).
- [x] **MongoDB descartado como provedor transacional** — 2026-05-01 (`820843c`). ADR 0001 formal em `docs/adr/0001-mongo-discarded.md`.
- [x] **Seed bootstrap idempotente em SQL** — 2026-05-06 (`d754f84`, `93f4b4f`, `01eb29b`, `46a0c07`). 4 camadas: Npgsql direto + retry strategy compatível + `[NotMapped]` em IsSeedData + per-tenant files.
- [x] **Notifications PR1–PR7 completo** — 2026-05-06. Outbox + adapters + Worker + painel Admin + LGPD + métricas OTel.
- [x] **Helpdesk core E2E** — 2026-05-06. AdminTicket reformado + SLA monitor + 9 eventos globais + UI multi-nível.
- [x] **Dual frontend formalizado** — 2026-05-06 (`4e9ffda`). Política PWA → MAUI unidirecional em `dual-frontend-policy.md`.
- [x] **Cascata API+Worker mitigada (parcial)** — 2026-05-07. `/health/dispatcher` separado de `/health/api`; heartbeat singleton dos 3 loops Hosted; warning de startup quando `Notifications:Hosting:Mode=Hosted` na API. Bulkhead real continua sendo Worker como deploy separado (default).
- [x] **B-015 — Rate limit em `/api/auth/login` e `/api/auth/register`** — 2026-05-07. Policy `auth` fixed-window 10/min/IP cobre login + register + refresh + forgot-password + reset-password. Teste em `AuthRateLimitTests.cs`. Webhook Pix segue como item residual.

---

## Como atualizar este arquivo

1. Resolveu um item? Move pra "Itens resolvidos" com data + commit hash.
2. Descobriu novo gap operacional (não feature)? Adiciona em `[ ]` no bloco que faz sentido.
3. Mudou prioridade? Reordena dentro do bloco — não deleta.
4. Atualiza "Última atualização" no topo.
5. Commita com `docs(stability): ...`.
