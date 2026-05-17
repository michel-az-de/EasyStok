# Sessão Fase 1 — Rebase master local em origin/master

Data: 2026-05-16 12:25 BR
Worktree: master principal (sem worktree dedicada)
Identidade Git: felipe.azevedo@gmail.com (commits) / michel-az-de (GitHub)
Status final: completo

## O que foi feito

Rebase Rota A: master local rebaseado em cima de origin/master.

- Working tree dirty preservado via `git stash push -u`.
- Cherry-pick do fix NU1605 (commit `1c088bbb` da branch deletada `fix/mongo-integration-tests-di-version`) para master, novo SHA `827287fc`.
- CLAUDE.md v1.0 commitado direto em master (R1 exceção autorizada), SHA `3e7c1013`.
- Backup criado: branch `backup/master-pre-fase1-2026-05-16` apontando para 352180ba.
- Rebase de 25 commits em cima de origin/master HEAD `040daea4`.
- Conflitos resolvidos (4 arquivos, 6 conflitos no total):
  - `Dashboard/Index.cshtml`: mergeado `fmt.money/fmt.date` (polish helpers) + `statusMap` (Onda B necessário para statusBadge logo abaixo).
  - `Pedidos/Detail.cshtml` (3 conflitos): preservado `text-slate-400 line-through` quando cancelado (Onda A) + `tabular-nums` (polish) + `AsMoney()` (polish) + `@if (!isCancelado)` wrapper (Onda A) + `ToString("0.##", pt-BR)` para Quantidade (Onda B fix locale "3,000").
  - `Entradas/Historico.cshtml`: preservado `ToString("0.##", ptBr)` para Qty (Onda B fix) + `AsMoney()` para money + `col-num` (polish).
  - `Lotes/Detail.cshtml`: preservado inline edit de peso + badge Embalado + Backfill (trabalho LOTES/TipoEmbalagem do stash) + `ToString("0.##", pt-BR)` para Quantidade (Onda B fix).
- Build pós-rebase: 0 erros, 30 warnings pré-existentes.
- Architecture tests: 6/7 pass (1 falha pré-existente catalogada).
- Push: `040daea4..d3d5bb58 master -> master`.
- Stash pop: aplicou trabalho preservado (LOTES/ETIQUETA/POLISH-residual/MOBILE) com 1 conflito em `Lotes/Detail.cshtml` (resolvido), 2 arquivos JS já em sync com HEAD (descartados local antes do pop).

## O que ficou pendente

- **Fase 2**: 4 PRs sequenciais do trabalho preservado no working tree:
  - PR-1: LOTES/TipoEmbalagem (28 modified + 4 untracked)
  - PR-2: ETIQUETA (7 modified)
  - PR-3: POLISH UI residual (6 modified + Dockerfile)
  - PR-4: MOBILE sync (2 modified)
- **Fase 3**: Higiene do repo (worktrees auto-gerados, branches dangling, stashes antigos, MSB3277 EF Relational, flaky-tests.md update).

## Decisões tomadas

- Rota A confirmada: master local rebaseado em origin/master (preserva ambos os trabalhos).
- Conflitos resolvidos mergeando intenção das Ondas A/B/C/D (fixes funcionais) com Polish UI (helpers, tabular-nums, col-num design system).
- Stash dropado após pop bem-sucedido (não preserva mais cópia).
- 1 cherry-pick + 1 commit direto em master autorizados como R1 exceção retroativa para fechar Fase 0 contaminada por PR #141.

## Commits criados em master

| SHA original | SHA pós-rebase | Descrição |
|---|---|---|
| (cherry-pick de 1c088bbb) → 827287fc | b73b1fbf | fix(deps): bump DI 9.0.0 -> 9.0.4 |
| 3e7c1013 (direto) | d3d5bb58 | docs(policy): CLAUDE.md v1.0 |
| Os 23 commits locais anteriores foram preservados com SHAs novos | — | (polish, p-02, fiscal, etc.) |

HEAD master/origin/master = `d3d5bb58`.

## Branches/worktrees

- Criada: `backup/master-pre-fase1-2026-05-16` (segurança, apontando para 352180ba).
- Deletada: `fix/mongo-integration-tests-di-version` (local + remoto) via `gh pr close 141 --delete-branch`.
- Removida: worktree `wt-mongo-di-fix` (--force, untracked husky helpers).

## Próxima ação recomendada

Iniciar Fase 2 imediatamente (este handoff foi criado entre Fase 1 e Fase 2; nenhuma pausa necessária se a sequência seguir o roadmap).

## Referências

- Plano original: docs/plan/p-02-rotulagem-nutricional.md (não modificado)
- Incidentes: docs/dev/incidentes/2026-05-16-*
- CLAUDE.md v1.0 commitado em `d3d5bb58`
- ADRs novos no chain: 0011-0017
