# Stability Roadmap — EasyStock

> **Para o próximo Claude:** este arquivo lista o que falta pra **estabilizar** as features que já existem (não features novas). Sempre que algo aqui for resolvido, **marque com `[x]` e adicione data + commit hash**. Sempre que descobrir um novo gap operacional, **adicione na seção apropriada** com `[ ]`. Não delete itens resolvidos — vire-os check pra preservar histórico.
>
> Princípio: **código está em ~40% parity, operação em ~15%**. Estabilizar = fechar deploy, CI, backup, alerta, testes de integração.

Última atualização: 2026-05-01

---

## Bloco 1 — Confiabilidade (P0)

- [ ] **Deploy estável GCP**: Cloud Run + Cloud SQL com 1 prod + 1 staging. Dockerfiles prontos (`Dockerfile.cloudrun.{api,web,admin}`). Aguarda Project ID. Ver `.knowledge/gcp-deploy.md`.
- [ ] **Backup Postgres + restore testado**: Cloud SQL faz daily, mas restore precisa ser exercitado. Documentar passo-a-passo no runbook.
- [ ] **CI rodando 474 testes em cada PR**: GitHub Actions com `dotnet build` + `dotnet test` + lint. Bloquear merge se vermelho.
- [ ] **Health checks reais** (`/health/live`, `/health/ready`) usados pelo Cloud Run pra restart automático. Middleware existe parcial — conferir e completar.
- [ ] **Compras → estoque ponta-a-ponta testado**: `PedidoFornecedorItem` foi criada (commit recente) mas fluxo de recebimento → entrada de movimentação **sem teste de integração**.

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
- [ ] **Rate limiting** nos endpoints públicos: `/auth/login`, `/auth/registrar`, `/api/webhooks/pix`.
- [ ] **Auditoria de `[AllowAnonymous]` restantes**: DiagnosticoController OK; conferir Mobile e outros Webhooks.
- [ ] **HTTPS-only + HSTS** no Cloud Run.
- [ ] **CSP / anti-XSS** no Web e Admin (Razor escapa por padrão, mas inline scripts são comuns).

## Bloco 5 — Tech debt que vira incidente (P1)

- [ ] **Decisão `Infra.MongoDb`**: mata ou alinha. Hoje código parcial divergente do Postgre, com fallback exótico em transação que vai dar bug em prod.
- [ ] **Cache no `SubscriptionGateMiddleware`**: bate no DB toda request autenticada. Em escala = latência cumulativa.
- [ ] **Auditoria de `Math.Round`/`Quantidade`** em todas movimentações: transferência, devolução, ajuste, inventário (já fixado em saída de pedido).
- [ ] **Idempotência de webhook duplicado**: Efí pode mandar 2x simultâneo dentro da janela de replay (5 min) — race condition possível em `ProcessarPagamentoAsync`.

## Bloco 6 — Operacional (P2)

- [ ] **Runbook básico** (`docs/runbook.md`):
  - Restaurar backup PG
  - Suspender tenant
  - Reprocessar webhook Pix perdido
  - Rollback Cloud Run revision
  - Rotacionar secret
- [ ] **Migrations idempotentes auditadas** (lição do `AddAdminModule` duplicando tabela).
- [ ] **Seed de demo** confiável pra onboarding e screenshot.
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
- [x] **`PedidoFornecedor.Itens [NotMapped]`** — 2026-04-30 (commits da série). Entity `PedidoFornecedorItem` persiste itens.
- [x] **`DiagnosticoController` exposto** — 2026-04-30. Class agora `[Authorize(Policy="Admin")]`.
- [x] **Pedido → estoque com idempotência por `{pedidoId}:{itemId}`** — 2026-04 (commit `340aff0`).

---

## Como atualizar este arquivo

1. Resolveu um item? Move pra "Itens resolvidos" com data + commit hash.
2. Descobriu novo gap operacional (não feature)? Adiciona em `[ ]` no bloco que faz sentido.
3. Mudou prioridade? Reordena dentro do bloco — não deleta.
4. Atualiza "Última atualização" no topo.
5. Commita com `docs(stability): ...`.
