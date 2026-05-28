# Nota de arquivamento — experimento sistema ETK

**Data de arquivamento:** 2026-05-28
**ADR que arquivou:** [ADR-0022 master-first trunk-based](../../../adr/0022-master-first-trunk-based.md)
**ADR que foi superseded:** [ADR-0020 tasks ETK-NNNN + TDD + multitarefa](../../../adr/0020-tdd-tasks-numeradas-multitarefa.md)

## Estado final do experimento

| Pasta         | Quantidade |
|---------------|------------|
| done          | 2          |
| backlog       | 32         |
| in-progress   | 0          |
| blocked       | 0          |
| inbox         | 21         |
| locks         | 0          |
| **TOTAL**     | **55**     |
| **Conclusão** | **3.6%**   |

Período do experimento: 2026-05-24 → 2026-05-28 (4 dias).

## Por que falhou

1. Overhead alto (claim + heartbeat + complete + worktree + branch + PR + TDD em 3 commits)
   incompatível com throughput natural do projeto (1172 commits master / 60 dias).
2. Paralelismo era prematuro (projeto solo: 1 dev + 1 agente Claude por vez).
3. Branches feat/etk-* acumularam sem mergear; PRs travaram em conflict.
4. Commit `d030e837` já documentava drift "TDD direto master" — política formal
   contradizia a prática real.

## Por que arquivar (não deletar)

- 32 tasks no backlog podem ser fonte de planejamento futuro.
- Histórico do experimento é dado útil pra futuras decisões.
- Schema YAML em `_schema.md` pode ser reaproveitado se sistema similar for tentado.

## O que ler em vez disso

- `docs/plan/` — planejamento canônico (sem overhead ETK)
- `docs/adr/0022-master-first-trunk-based.md` — política substituta
- `CLAUDE.md` v3.0 — protocolo operacional vigente
