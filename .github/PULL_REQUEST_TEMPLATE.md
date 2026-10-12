<!-- Titulo do PR = Conventional Commit (vira a mensagem do squash no master). -->

closes #<N>

## O que muda
<!-- Resumo objetivo da mudanca. -->

## Tier
<!-- baixo (chore/docs/test/fix-trivial, ou spec S/P de plano aprovado -> merge no verde, ADR-0055) | alto (migracao/auth/RLS/policy, ou feat/refactor sem spec -> aguarda label `aprovado`) -->
<!-- Spec: S__ (docs/plan/...) -->

## Verificacao
- [ ] gate.ps1 verde (build + arch) localmente
- [ ] `changelog.d/<issue>.md` criado (nao editar o topo do CHANGELOG.md)
- [ ] /code-review + pr-review-toolkit sem Critical
- [ ] Aceite da issue #<N> todo marcado

## Notas
<!-- Racional de mudanca grande (fatiamento), gotchas, follow-ups. -->
