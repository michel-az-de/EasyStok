# Recent Evolution

> Resumo curado das ondas de feature mais recentes. Atualizar manualmente após sessão grande.

## Snapshot 2026-05-07

### Onda atual: CI gate de cobertura (Fase 1)
- Workflow `.github/workflows/tests.yml` em pull_request e push pra master/main: build Release + 6 projetos de teste com `--collect:"XPlat Code Coverage"` + `coverlet.runsettings` na raiz com excludes (Migrations, Program.cs, DTOs, geradores, Mobile, Worker, Mongo, Sqlite, Async).
- Cobertura agregada via ReportGenerator → HTML/Cobertura/Markdown/Badges. Job summary + comentário sticky no PR (`marocchino/sticky-pull-request-comment`). TRX → check via `dorny/test-reporter`.
- Gate baseline: line 10% / branch 5% — não bloqueia PRs hoje, mas detecta drop crítico. Fase 2 sobe pra 90% line nos paths Pix/Webhook/Faturas.
- Scripts locais `scripts/coverage.{ps1,sh}` espelhando o workflow pra Felipe rodar offline.

### Ondas concluídas (últimos 30 dias)
- **Notifications PR1–PR7**: domain → ports → adapters Email/SMS/WhatsApp/InApp → Worker com Outbox → painel Admin → migração jobs legados → LGPD self-service + métricas OTel.
- **Helpdesk core**: AdminTicket reformado (TicketAnexo, TicketHistorico, AdminTicketTecnicoMeta, SlaConfiguracao), SlaMonitorService, 9 eventos de notificação globais, UI Tickets multi-nível.
- **Admin Gestão de Cliente P0–P3**: reset senha, força logout, sessões, edição usuário, CRUD lojas, atividade unificada, PII unmask + audit, LGPD anonimizar/exportar, Cmd+K + atalhos + recentes, notas internas + banner alerta.
- **Mobile MAUI F0–F4c**: setup → estrutura → auth E2E (JWT+refresh+biometria) → multi-tenant pickers + permissões → produção SQLite + REST → mutations otimistas (outbox SQLite) → SyncEngine periódico → popup captura foto/peso/validade.
- **Seed bootstrap idempotente**: 4 camadas de defesa (Npgsql direto + retry strategy compatível + `[NotMapped]` em IsSeedData + per-tenant files + script `CreateSuperAdmin`).
- **Dual frontend formalizado**: política PWA → MAUI unidirecional documentada em `dual-frontend-policy.md` (commit `4e9ffda`).

### Decisões de arquitetura recentes
- **2026-05-01**: ADR 0001 — MongoDB descartado como provedor transacional (`docs/adr/0001-mongo-discarded.md`). Postgre é único.
- **2026-05-01**: P0 Webhook Pix validação de valor pago (commit `37fb7d9`).
- **2026-04-30**: P0 `PedidoFornecedorItem` criada — Compras agora persiste itens.
- **2026-04**: PedidoEstoqueIntegrationService extraído com `PedidoEstoqueOptions`.
- **2026-04**: `AtualizarStatusPedidoUseCase` com state machine matrix explícita + `GetByIdWithDetailsAsync`.
- **2026-04**: xmin RowVersion em Produto, Pedido, ItemEstoque, AssinaturaEmpresa.
- **2026-04**: SubscriptionGateMiddleware retornando 402 para tenants suspensos.
- **2026-04**: Webhook Pix com HMAC-SHA256 + replay protection (com validação de valor adicionada em maio).

### Direção atual
- Foco P0 atual: NF-e (decidir emissor third-party), rate limiting endpoints públicos, CI gate de qualidade.
- Helpdesk continua em maturação (próxima onda: SLA UI + reabertura + escalação).
- Mobile MAUI saindo de F4 — próxima onda: bridge JS↔Native dentro do WebView.
- Render NÃO é deploy alvo (referência antiga em memória ignorada). Hoje é Azure App Service via `deploy-azure.yml`. GCP é plano alternativo documentado.

### Últimos 10 commits
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
