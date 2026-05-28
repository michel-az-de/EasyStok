# scripts/tasks/ — ARQUIVADO

Scripts do sistema ETK (ADR-0020) arquivados em 2026-05-28 via [ADR-0022](../../../docs/adr/0022-master-first-trunk-based.md).

## Conteúdo

- `claim.sh` — claim atômico de task (criava worktree + lock + branch)
- `heartbeat.sh` — heartbeat a cada 20min pra evitar lock stale
- `complete.sh` — quality gates + move pra done/ + push branch
- `validate.sh` — sanity check pré-claim
- `regen-index.sh` — regenera `docs/tasks/_index.yaml`

## Estado dos arquivos

Mantidos como referência histórica. **Não usar.** O sistema foi descontinuado.

Política vigente: master-first trunk-based. Ver `CLAUDE.md` v3.0.

## Se quiser ressuscitar parte disso

- `regen-index.sh` poderia ser reaproveitado pra qualquer índice YAML futuro
- `validate.sh` tem lógica de sanity check útil
- `complete.sh` tem matriz de quality gates por tipo de mudança
