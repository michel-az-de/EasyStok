# Recent Evolution

> Resumo curado das ondas de feature mais recentes. Atualizar manualmente após sessão grande.

## Snapshot 2026-05-07

### Onda Billing/Faturas F1–F14 (1 dia)
Sessão massiva reativando e ampliando o módulo financeiro:

- **F1+F2**: reativação completa do módulo Faturas (DbSets + FaturaNumeradorService).
- **F3**: abstração multi-gateway (`IPagamentoGateway`, `PagamentoGatewayRouter`) + webhook genérico idempotente.
- **F4**: geração de PDF de fatura via QuestPDF.
- **F5**: convivência Fatura ↔ CobrancaAssinatura SaaS.
- **F6**: reconciliação automática + notificações de vencimento (`FaturaReconciliacaoJob`).
- **F7**: UI Admin de Faturas (listagem, detalhe, emissão avulsa).
- **F8**: portal cliente standalone para faturas.
- **F9**: integração Tickets ↔ Faturas + Export CSV.
- **F10**: dashboard financeiro com MRR, ARR, churn, top inadimplentes. (`74a2b08`: MetricasFinanceirasUseCase, endpoint `GET /api/admin/faturas/metricas`).
- **F11**: consulta + estorno Pix Efí (`ConsultarCobrancaAsync`, `EstornarAsync`) destrava reconciliação real (`9d64666`).
- **F12**: adapters stub Stripe + MercadoPago + signature validators (HMAC-SHA256 com header próprio de cada provider) (`e5a4180`).
- **F13**: cache de métricas financeiras com TTL 5min (`99acab7`: IMemoryCache, chave `metricas:{empresaId|all}:{dias}`, ForcarRefresh wireado no controller).
- **F13 fix**: Multi-tenant leak em assinaturas (`191f685`): SomarPrecoMensalAtivasAsync/ContarPorStatusAsync agora aceitam empresaId opcional.
- **F14**: trigger automático de ticket Financeiro após N falhas de pagamento (`e29cc61`: IFalhaPagamentoNotifier + AutoTicketFalhaPagamento, threshold 3 falhas/7 dias).
- **F14 fix**: webhook anônimo violava FK em CriadoPorId (`b11d165`, [PR #70](https://github.com/michel-az-de/EasyStok/pull/70)): coage `currentUser.UsuarioId == Guid.Empty → null` em HelpdeskTicketService.AbrirAsync. Lição 14 em do-not-do.md.
- **Hardening webhook Pix**: 5 correções (race em duplo-fire com `FOR UPDATE` em txid, validação de valor pago etc).
- **Multi-tenant em métricas**: dashboard não vaza MRR/contagens globais a admin operacional.
- **Cleanup billing**: dois passes (4f9c259 e fb4f08e) removendo código morto, padronizando usings, corrigindo comentários stale (FornecedorRepository Mongo Audit P4 não era stub).

### Lições registradas
- **Lição 12**: multi-tenant em agregação (`SomarPrecoMensalAtivasAsync(empresaId?)` com filtro opcional).
- **Lição 13**: XML doc em `record` types (props públicas precisam de `<param>` no construtor primário).

### Outras ondas concluídas hoje
- **Landing pages**: site público (index/preços/app/contato/sucesso) + LeadPublico anônimo + rate limit + anti-spam.
- **Integrations Fase 2/3/4**: `EasyStock.Contracts` + `EasyStock.Infra.Integrations` + `IntegrationCredentialResolver` AES-256-GCM + outbox transacional + `IntegrationEventDispatcher` Worker.
- **API docs**: Swagger UI navy + laranja, console futurista, JWT try-it.
- **Seed**: SuperAdmin global idempotente no startup + bootstrap em 4 camadas.

## Snapshot 2026-05-06

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
- **2026-05-07**: B-015 fechado — policy `auth` (fixed-window 10/min/IP) já cobria login/register/refresh/forgot/reset; teste de regressao adicionado (`AuthRateLimitTests.cs`). Webhook Pix segue como item residual do P0 #2.
- Foco P0 atual: NF-e (decidir emissor third-party), rate limiting em `/api/webhooks/pix`, CI gate de qualidade.
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
