# v1.0 — Marco Zero EasyStok

Diretório dedicado à estabilização do produto até a tag `v1.0` (Marco Zero — primeiro release com escopo congelado, features E2E testadas e operação observável).

**Plano fonte:** `~/.claude/plans/vamos-criar-ent-o-um-functional-gosling.md` (aprovado em 2026-05-26).
**Snapshot base:** master @ `c4e2e84b` (2026-05-26).

---

## Estrutura

| Arquivo | Conteúdo | Fase |
|---|---|---|
| [SCOPE.md](SCOPE.md) | Features DENTRO/FORA do v1.0 + governança de escopo | 0 |
| [GOLDEN-PATHS.md](GOLDEN-PATHS.md) | 33 fluxos E2E críticos por persona × feature | 0 |
| [TECH-DEBT.md](TECH-DEBT.md) | Débito técnico documentado para v1.1+ | 0 |
| AUDIT-RESULTS.md | (a gerar na Fase 1) inventário de gaps por golden path | 1 |

---

## Fases (visão executiva)

1. **Fase 0 — Congelar escopo + Golden Paths** *(esta sessão)*
   Entregáveis: este diretório.
2. **Fase 1 — Auditoria funcional** *(chat novo)*
   Rodar cada golden path em staging, criar ETK-AUDIT-NNN para gaps.
3. **Fase 2 — Defesas estruturais** *(chats novos em paralelo)*
   ETK-0002 (branch protection), ETK-0020 (CI), ETK-0025 (OTel), + ETK-DEV-001 / ETK-SMOKE-001 / ETK-BACKUP-001 (novos).
4. **Fase 3 — Fechar P0/P1** *(chats novos sequenciais)*
   Consumir ETK-AUDIT-NNN seguindo ADR-0020.
5. **Fase 4 — Testes E2E automatizados** *(chat novo)*
   Playwright em `tests/e2e/`, integrado ao CI.
6. **Fase 5 — Freeze + tag v1.0** *(chats novos)*
   ETK-FREEZE-001/002/003 + ETK-RELEASE-001.

Cada fase tem prompt-de-arranque escrito no plano fonte. Dependências e critérios de "feito" estão lá também.

---

## Personas (referência cruzada com GOLDEN-PATHS.md)

| Código | Persona | Papel |
|---|---|---|
| A | Admin Loja | dono ou gerente — cadastra, configura, vê relatórios |
| B | Vendedor / Operador Caixa | opera o dia a dia — venda, caixa, atendimento |
| C | Cliente Storefront | consumidor final B2C — navega, pede, paga, recebe |
| D | Sistema | jobs background, integrações, webhooks |

---

## Referências obrigatórias

- **Protocolo operacional:** [CLAUDE.md](../../../CLAUDE.md) v2.0
- **ADRs principais:**
  - [ADR-0010](../../adr/0010-rls-postgres-defesa-em-profundidade.md) — RLS multi-tenant
  - [ADR-0013](../../adr/0013-cancellation-token-iusecase.md) — CancellationToken Deferred
  - [ADR-0018](../../adr/0018-nfe-asterisco-em-codigo.md) — Nfe* em código
  - [ADR-0019](../../adr/0019-mobile-controllers-response-pattern.md) — 2 superfícies HTTP
  - [ADR-0020](../../adr/0020-task-numerada-tdd-worktree.md) — TDD + worktree por task
  - [ADR-0021](../../adr/0021-rotulagem-p02-etapa5-do-roadmap.md) — Rotulagem é Etapa 5
- **Sistema ETK:** `docs/tasks/{backlog,done,blocked}/` (governado por ADR-0020)
- **Plano de domínio existente:** `docs/plan/00..08-*.md` + `docs/plan/nota-fiscal/`

---

## Como atualizar este diretório

Mudanças em `SCOPE.md` ou `GOLDEN-PATHS.md` **durante** as Fases 1-5 exigem:
1. PR dedicado (`docs(v1.0): <motivo>`) explicando o porquê.
2. Atualização cruzada de TECH-DEBT.md se algo sai do escopo.
3. Atualização de AUDIT-RESULTS.md se já existir.

Não editar este diretório como subproduto de PRs de feature.
