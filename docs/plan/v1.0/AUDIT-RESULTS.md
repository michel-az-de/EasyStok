# AUDIT-RESULTS.md — Auditoria funcional dos 33 golden paths v1.0

> [!WARNING]
> **OBSOLETO-PARCIAL** desde 2026-05-27 ~20:00 UTC.
>
> Master avançou 13 commits do Felipe entre PR #248 (auditoria mergeada 17:27 UTC) e PR #250 (handoff mergeado 19:42 UTC), implementando TASK-EZ-AVAL-001 (5 commits TDD), TASK-EZ-APROVAR-001 (5 commits TDD), TASK-EZ-PEDIDOS-001 e TASK-EZ-WEBHOOK-001 (base CHECKOUT-001). Provavelmente invalida ~50% dos achados deste relatório:
> - **ETK-AUDIT-001** (checkout storefront) → `IniciarCheckoutUseCase` + `CheckoutController` agora em master
> - **ETK-AUDIT-002** (Pedido→PIX) → gateway mudou Efi → MercadoPago (`IMercadoPagoClient`); novo status `AguardandoPagamento`
> - **ETK-AUDIT-003** (estorno em cancelar) → `AprovarPedidoStorefrontUseCase` + `RecusarPedidoStorefrontUseCase` existem; precisa confirmar se recusa estorna
> - ETK-AUDIT-004..009 — provavelmente ainda válidos
>
> **Próxima ação:** Fase 1.5 — re-auditoria em sessão Claude nova sobre master atual. Registro do drift e perguntas para Felipe em [docs/dev/incidentes/2026-05-27-tdd-direto-master-tasks-etk.md](../../dev/incidentes/2026-05-27-tdd-direto-master-tasks-etk.md).

**Sessão:** Fase 1 — 2026-05-27
**Branch:** `docs/audit-fase-1-marco-zero`
**Worktree:** `wt-audit-v1-fase-1`
**Base:** master @ `e78dc493`
**Método:** auditoria estática (code-review) — não rodado em staging/local
**Razão da escolha do método:** auditoria local exigia subir Postgres + seed + Web/Admin/Storefront com subdomínios, Efi sandbox, SMTP stub, WebPush HTTPS — fricção alta demais para 33 paths em 1 sessão. Auditoria estática cobre 33/33 com fundamentação técnica concreta (file:line) e é reproduzível pelo próximo agente. Bugs runtime são responsabilidade da Fase 4 (Playwright E2E que ainda não existe).

---

## Sumário executivo

| Status | Qtd | % |
|---|---|---|
| PASS | 25 | 76% |
| PARCIAL | 3 | 9% |
| FAIL | 2 | 6% |
| INDISPONÍVEL | 3 | 9% |
| **Total** | **33** | **100%** |

**ETKs gerados:** 9 (3 P0 críticos, 3 P1 importantes, 3 P2 polimento). Ver mapeamento em [§5](#5-etks-gerados).

### Recomendação v1.0

**Marco Zero v1.0 NÃO é alcançável com o escopo atual** sem fechar os 3 ETKs P0 abaixo:

1. **[ETK-AUDIT-001](../../tasks/backlog/ETK-AUDIT-001-checkout-storefront-e2e.yaml)** — Checkout storefront end-to-end (resolve GP-017, GP-023, GP-029)
2. **[ETK-AUDIT-002](../../tasks/backlog/ETK-AUDIT-002-orquestracao-pedido-pix.yaml)** — Orquestração Pedido→PIX (resolve GP-022, parte de GP-023/029)
3. **[ETK-AUDIT-003](../../tasks/backlog/ETK-AUDIT-003-estorno-pix-cancelar-pedido.yaml)** — Estorno PIX + devolução estoque em cancelar (resolve GP-018, GP-030)

Estes 3 ETKs sozinhos destravam **6 dos 33 GPs** (18% do escopo), incluindo o super-path **GP-029 — "golden path nº 1 do v1.0"**.

**Alternativa de escopo** (não recomendada nesta sessão, mas vale registrar): se a Fase 3 atrasar, considerar tirar Storefront do escopo v1.0 (mover para v1.1+) e lançar v1.0 com **Admin + Vendedor + Balcão + Financeiro** — 25/26 GPs PASS (≈100%). Tradeoff: Storefront é o diferencial declarado em [SCOPE.md](SCOPE.md#dentro-12-features).

---

## 1. Tabela detalhada por GP

Coluna **Status** reconciliada entre 4 agentes Explore + 1 verificação direta do Claude principal (confronto de evidências, leitura de use cases conflitantes). Conflitos importantes: GP-010, GP-017, GP-018, GP-029, GP-030.

| GP | Título | Status | P0 | P1 | P2 | ETK | Persona |
|---|---|---|---|---|---|---|---|
| GP-001 | Login Admin | PASS | — | — | — | — | A |
| GP-002 | Login Vendedor (Web) | PASS | — | — | — | — | B |
| GP-003 | Login Cliente (OTP storefront) | PARCIAL | — | — | 1 | [007](../../tasks/backlog/ETK-AUDIT-007-defesa-reuso-otp.yaml) | C |
| GP-004 | Refresh token automático | PASS | — | — | 1 | [008](../../tasks/backlog/ETK-AUDIT-008-header-refreshed-token.yaml) | D |
| GP-005 | Criar/configurar loja inicial | PASS | — | — | — | — | A |
| GP-006 | Cadastrar produto + categoria + variação | PASS | — | — | — | — | A |
| GP-007 | Atualizar preço e estoque inicial | PASS | — | — | — | — | A |
| GP-008 | Entrada de estoque manual | PASS | — | — | — | — | A |
| GP-009 | Criar lote de produção | PASS | — | — | — | — | A |
| GP-010 | Baixa automática via venda | PASS | — | — | — | — | D |
| GP-011 | Cadastrar fornecedor | PASS | — | — | — | — | A |
| GP-012 | Sugestão de compra → pedido fornecedor | PASS | — | — | — | — | A |
| GP-013 | Receber mercadoria → atualizar estoque | PASS | — | 1 | — | [006](../../tasks/backlog/ETK-AUDIT-006-conta-pagar-pos-recebimento.yaml) | A |
| GP-014 | Cadastrar cliente (admin) | PASS | — | — | — | — | A |
| GP-015 | Autocadastro cliente via storefront | PASS | — | — | — | — | C |
| GP-016 | Criar pedido balcão completo | PASS | — | — | — | — | B |
| GP-017 | Criar pedido via storefront | **INDISPONÍVEL** | 1 | — | — | [001](../../tasks/backlog/ETK-AUDIT-001-checkout-storefront-e2e.yaml) | C |
| GP-018 | Revisar/aprovar pedido | **FAIL** | 1 | — | — | [003](../../tasks/backlog/ETK-AUDIT-003-estorno-pix-cancelar-pedido.yaml) | A |
| GP-019 | Abrir caixa | PARCIAL | — | 1 | — | [005](../../tasks/backlog/ETK-AUDIT-005-caixa-um-por-vendedor-turno.yaml) | B |
| GP-020 | Registrar movimento (sangria/suprimento) | PASS | — | — | — | — | B |
| GP-021 | Fechar caixa + ver relatório | PASS | — | — | 1 | [009](../../tasks/backlog/ETK-AUDIT-009-export-csv-fechamento-caixa.yaml) | B/A |
| GP-022 | Pagar pedido balcão via PIX | PARCIAL | 1 | — | — | [002](../../tasks/backlog/ETK-AUDIT-002-orquestracao-pedido-pix.yaml) | B |
| GP-023 | Pagar pedido storefront via PIX | **INDISPONÍVEL** | 1 | — | — | [001](../../tasks/backlog/ETK-AUDIT-001-checkout-storefront-e2e.yaml) + [002](../../tasks/backlog/ETK-AUDIT-002-orquestracao-pedido-pix.yaml) | C |
| GP-024 | Reconciliação automática webhook Efi | PASS | — | — | — | — | D |
| GP-025 | Email de confirmação de pedido | PASS | — | — | — | — | D |
| GP-026 | Push de status de pedido | PASS | — | — | — | — | C |
| GP-027 | Configurar templates de notificação | PASS | — | — | — | — | A |
| GP-028 | Configurar cardápio + zonas + janelas | PASS | — | — | — | — | A |
| GP-029 | Pedido completo no storefront E2E (super-path) | **INDISPONÍVEL** | 1 | 1 | — | [001](../../tasks/backlog/ETK-AUDIT-001-checkout-storefront-e2e.yaml) + [002](../../tasks/backlog/ETK-AUDIT-002-orquestracao-pedido-pix.yaml) + [004](../../tasks/backlog/ETK-AUDIT-004-sse-status-pedido-storefront.yaml) | C |
| GP-030 | Aprovar/recusar pedido storefront | **FAIL** | 1 | — | — | [003](../../tasks/backlog/ETK-AUDIT-003-estorno-pix-cancelar-pedido.yaml) | A |
| GP-031 | Criar conta a pagar | PASS | — | — | — | — | A |
| GP-032 | Dar baixa em conta a receber | PASS | — | — | — | — | D |
| GP-033 | Relatório financeiro do dia | PASS | — | — | — | — | A |

**Legenda:** ETKs aparecem múltiplas vezes quando 1 ETK resolve gaps em vários GPs.

---

## 2. Top 5 paths mais arriscados

Ordenados por gravidade (INDISPONÍVEL > FAIL > PARCIAL × criticidade de produto):

### 2.1. GP-029 — Pedido completo no storefront E2E (INDISPONÍVEL)

**Por que é o nº 1:** auto-declarado "golden path nº 1 do v1.0" em [GOLDEN-PATHS.md:626](GOLDEN-PATHS.md#gp-029-pedido-completo-no-storefront-e2e-super-path). Depende de GP-017 + GP-023 + GP-018/030 — **todos com bloqueador P0**. Componentes-folha (Auth, Menu, Frete, Agendamento, Webhook PIX, Notificações Email+Push) estão maduros, mas o **cliente storefront não consegue criar e pagar um pedido em master**. CheckoutIdempotency entity existe mas o use case que a usa está apenas na branch paralela `feat/task-ez-agend-001-listar-janelas`.

### 2.2. GP-018 — Revisar/aprovar pedido (FAIL)

**Bug funcional crítico:** `CancelarPedidoUseCase` em [EasyStock.Application/UseCases/CancelarPedido/CancelarPedidoUseCase.cs:35-52](../../EasyStock.Application/UseCases/CancelarPedido/CancelarPedidoUseCase.cs) apenas muda status + registra evento. NÃO chama estorno PIX, NÃO chama devolução de estoque. `AtualizarStatusPedidoUseCase` com status novo=Cancelado devolve estoque (✓) mas tampouco estorna PIX. **Cliente recusado fica sem dinheiro de volta** — viola critério "Recusa estorna PIX automaticamente (via Efi API)" da spec.

### 2.3. GP-030 — Aprovar/recusar pedido storefront (FAIL)

**Sofre exatamente o mesmo bug do GP-018** — usa os mesmos use cases. Pior: cliente storefront é menos forgivable que cliente balcão (não está fisicamente presente para reclamar).

### 2.4. GP-017 — Criar pedido via storefront (INDISPONÍVEL)

**Sem endpoint.** Não existe `CheckoutController` nem `PedidoController` em [EasyStock.Api/Controllers/Storefront/](../../EasyStock.Api/Controllers/Storefront/) (só Auth, Menu, Frete, Agendamento). Não existe `IniciarCheckoutUseCase` em [EasyStock.Application/UseCases/Storefront/](../../EasyStock.Application/UseCases/Storefront/) (só Auth, Avaliacao, Agendamento, Menu, Frete). Sem este path, todo o storefront é vitrine — não vende.

### 2.5. GP-023 — Pagar pedido storefront via PIX (INDISPONÍVEL)

**Depende em cascata de GP-017** e **falta orquestração específica Pedido→PIX**. `GerarPixQrParcelaReceberUseCase` existe mas é para `ParcelaContaReceber` (financeiro avulso), não para `Pedido`. Interfaces `IEfiPixService` + `IPagamentoOrchestrator` existem mas não há use case `GerarPixQrPedidoUseCase`.

---

## 3. Conflitos entre agentes (reconciliação)

Quatro agentes Explore foram disparados em paralelo. Quatro conflitos materiais foram detectados e reconciliados via leitura direta do código pelo Claude principal:

| Conflito | Agente A | Agente B | Reconciliação |
|---|---|---|---|
| **GP-010** automação baixa estoque | Agente 2: PARCIAL P0 ("sem domain event handler PedidoConfirmado→saída") | Agente 4: PASS (Notificações resolvem) | **PASS** — automação é via `AtualizarStatusPedidoUseCase` linhas 87-94 → `PedidoEstoqueIntegrationService.DescontarAsync()` (idempotente, FOR UPDATE). Não é via domain event handler (spec) mas é funcionalmente equivalente — atomicidade aplicação |
| **GP-017** checkout storefront | Agente 3: PARCIAL P0 | Agente 4: PASS (CheckoutIdempotency entity existe) | **INDISPONÍVEL P0** — entity existe, mas use case + controller não estão em master (estão em branch paralela `feat/task-ez-agend-001-listar-janelas`) |
| **GP-018/030** estorno PIX | Agente 3: FAIL P0 (CancelarPedidoUseCase não chama estorno) | Agente 4: PASS (EstornarPagamentoParcelaUseCase existe) | **FAIL P0** — `EstornarPagamentoParcelaUseCase` existe mas só é chamado por `ContasAReceberController`/`ContasAPagarController`, sem hook em `CancelarPedidoUseCase` |
| **GP-029** super-path | Agente 4: PARCIAL P1 (só SSE faltando) | — | **INDISPONÍVEL P0** — depende cascata de GP-017/023/018 que estão indisponíveis ou em FAIL |

**Lição metodológica:** agentes Explore com escopos diferentes têm visões diferentes do mesmo código. Reconciliação cruzada (verificar evidências citadas + procurar callers) é essencial. Os agentes 2 e 4 erraram para lados opostos (Agente 2 muito pessimista; Agente 4 muito otimista). Agente 3 acertou no diagnóstico crítico de GP-018.

---

## 4. Notas adicionais por GP (passes com observações)

Para os GPs marcados PASS mas com nota técnica relevante para Fase 3:

| GP | Nota | Risco |
|---|---|---|
| GP-005 | Auditor não confirmou RLS Postgres explicitamente (`current_setting('app.empresa_id')`) — presumido funcional via middleware | Médio — vale teste manual em staging |
| GP-005 | CNPJ via algoritmo Receita Federal não confirmado — presumido em VO `Cnpj.TryFrom()` | Baixo |
| GP-013 | Wire automático `ProcessarRecebimentoPedidoFornecedor` → `GerarContaPagarDePedidoFornecedor` não confirmado durante auditoria. Gerado ETK-AUDIT-006 para investigação dirigida | Médio |
| GP-019 | Validação "1 caixa por turno" frouxa — não bloqueia mas permite estado inconsistente. ETK-AUDIT-005 | Baixo (UX) |
| GP-021 | Export CSV/PDF não localizado em CaixaController. Pode estar em ReportsController ou inexistente. ETK-AUDIT-009 | Baixo (P2) |
| GP-024 | Rota é `/api/webhooks/pix`, não `/api/integrations/efi/webhook` (spec). Diferença documental ou de rota — corrigir um dos dois | Baixo (cosmético) |
| GP-024 | Reconciliação trata apenas `cr*` txids (ContaReceber); pedidos diretos via webhook usariam txid sem prefixo — gap potencial quando GP-022/023 ficarem prontos | Médio (revisitar após ETK-AUDIT-002) |

---

## 5. ETKs gerados

9 ETKs em `docs/tasks/backlog/`. Ordem de execução recomendada baseada em (a) prioridade, (b) dependências, (c) impacto sobre golden paths:

### P0 — Bloqueadores Marco Zero (3 ETKs, ~22h estimadas)

| ETK | Título | h | GPs afetados | Bloqueia |
|---|---|---|---|---|
| [ETK-AUDIT-001](../../tasks/backlog/ETK-AUDIT-001-checkout-storefront-e2e.yaml) | Checkout storefront E2E | 12 | GP-017, GP-023, GP-029 | ETK-AUDIT-002, ETK-AUDIT-004 |
| [ETK-AUDIT-002](../../tasks/backlog/ETK-AUDIT-002-orquestracao-pedido-pix.yaml) | Orquestração Pedido→PIX | 6 | GP-022, GP-023, GP-029 | — |
| [ETK-AUDIT-003](../../tasks/backlog/ETK-AUDIT-003-estorno-pix-cancelar-pedido.yaml) | Estorno PIX + devolução estoque em cancelar | 4 | GP-018, GP-030 | — |

### P1 — Importantes mas não bloqueiam release (3 ETKs, ~9h)

| ETK | Título | h | GPs afetados |
|---|---|---|---|
| [ETK-AUDIT-004](../../tasks/backlog/ETK-AUDIT-004-sse-status-pedido-storefront.yaml) | SSE status pedido storefront | 4 | GP-029 |
| [ETK-AUDIT-005](../../tasks/backlog/ETK-AUDIT-005-caixa-um-por-vendedor-turno.yaml) | Validação 1-caixa/vendedor/turno | 3 | GP-019 |
| [ETK-AUDIT-006](../../tasks/backlog/ETK-AUDIT-006-conta-pagar-pos-recebimento.yaml) | Wire automático ContaPagar pós-recebimento | 2 | GP-013 |

### P2 — Polimento / divergência de spec (3 ETKs, ~3.5h)

| ETK | Título | h | GPs afetados |
|---|---|---|---|
| [ETK-AUDIT-007](../../tasks/backlog/ETK-AUDIT-007-defesa-reuso-otp.yaml) | Defesa re-uso OTP consumido | 1 | GP-003 |
| [ETK-AUDIT-008](../../tasks/backlog/ETK-AUDIT-008-header-refreshed-token.yaml) | Header X-Refreshed-Token | 0.5 | GP-004 |
| [ETK-AUDIT-009](../../tasks/backlog/ETK-AUDIT-009-export-csv-fechamento-caixa.yaml) | Export CSV fechamento de caixa | 2 | GP-021 |

**Total estimado:** ~34.5h. P0 sozinho = ~22h. Realisticamente, com TDD obrigatório (ADR-0020), folga para integração + revisão de PR + handoff por ETK, dobrar: **~40-60h efetivas para fechar todos os P0+P1**, ou **~3-5 dias** com 1-2 chats Claude em paralelo.

---

## 6. Próximos passos

1. **Merge desta auditoria em master** (PR + admin squash, R9).
2. **Abrir 1 chat por ETK-AUDIT P0**, na ordem 001 → 003 → 002 (001 destrava 002; 003 é independente). Usar o prompt-de-arranque padrão da Fase 3 (ver [plano fonte](~/.claude/plans/vamos-criar-ent-o-um-functional-gosling.md)).
3. **Após P0 fechados**, re-auditar GP-017/018/022/023/029/030 e atualizar este AUDIT-RESULTS.
4. **Fase 2 em paralelo** (defesas estruturais — ETK-0020 CI billing, ETK-0025 OTel, ETK-DEV-001 onboarding local) — não dependem desta auditoria.
5. **Fase 4 (Playwright E2E)** só faz sentido depois de Fase 3 fechar todos os P0 — senão E2E falha imediato.

---

## 7. Apêndice — método

### 7.1. Worktree e branch

- Worktree: `C:\easy\EasyStok\.claude\worktrees\wt-audit-v1-fase-1`
- Branch: `docs/audit-fase-1-marco-zero`
- Base: master @ `e78dc493` (HEAD em 2026-05-27 ~13:30 UTC)
- Working tree principal está em `feat/task-ez-agend-001-listar-janelas` (sessão paralela TASK-EZ-AVAL-001 + AGEND-001) — não tocado, R5/R6.

### 7.2. Sessões paralelas detectadas (R5/R6)

9 worktrees ativos no momento da auditoria. Todos não-tocados.

- `wt-admin-002` → ETK-ADMIN-002 (zonas/janelas/bloqueios)
- `wt-ez-007` → entities avaliação/feedback
- `wt-ez-auth-002` → ValidarOtp + ClienteSession
- `wt-setup-local` → ETK-DEV-001 onboarding local
- `task-ez-aprovar-001-aprovar-recusar-pedido` (path legado)
- `task-ez-webhook-001-receive-then-process` (path legado)
- `menu-only` (recover/pr231-menu)
- `wt-tasks-bootstrap` (master)
- principal em `feat/task-ez-agend-001-listar-janelas`

### 7.3. Sanity-check de inventário (R10)

- Master compila VERDE (0 erros, 8 warnings pré-existentes — CA1422 Android, CS8602, CS9107, EF1002, MSB3277). Validado no worktree `wt-tasks-bootstrap`.
- O "build vermelho" inicial do inventário §0 do CLAUDE.md foi artefato — `dotnet build` rodou no working tree principal que estava em branch de feature paralela. **Sugestão de PR futuro:** §0 deveria qualificar branch ao testar build.
- Master ahead/behind origin: 0/0.
- Commit `e78dc493 feat(site): reformular landing comercial` em master foi commit direto (author=committer=Felipe, Co-Author Codex Opus 4.7, sem `(#NNN)` no título, 3 arquivos de landing comercial). Dentro da exceção R1 v2.1 (hotfix/doc < 1h, < 5 arquivos). Aceito sem reverter.

### 7.4. Limitação conhecida

Auditoria estática (code-review) detecta gaps de implementação, mas **não detecta**:

- Bugs runtime (race conditions sob carga real, timezone errado em produção, conn pool esgotado, etc.)
- Performance regressions
- UX issues (texto confuso, fluxo cansativo)
- Configuração faltante (Efi production creds, SMTP credentials, VAPID keys)
- Quebra de integração entre serviços rodando

Para esses, é necessária a Fase 4 (Playwright E2E em CI) + smoke synthetic (Fase 2 ETK-SMOKE-001) + observabilidade ativa (Fase 2 ETK-0025).
