# ADR-0022 — Master-first trunk-based, eliminacao do sistema ETK

**Status:** Aceito
**Data:** 2026-05-28
**Supersede:** ADR-0020 (TDD tasks ETK-NNNN + multitarefa por worktree)

## Contexto

ADR-0020 (2026-05-24) formalizou tasks ETK-NNNN + TDD obrigatorio + multitarefa
por worktree. Apos 4 dias de uso real, as medicoes mostram que o sistema falhou
em entregar:

```
docs/tasks/ em 2026-05-28:
  done:        2  (ETK-0004 roadmap publicado, ETK-0005 decisao-modulo)
  backlog:     32 (overload, fila infinita)
  in-progress: 0
  blocked:     0
  inbox:       21
  locks:       0
  Conclusao: 2 / 55 = 3.6% em 4 dias
```

Estado do repo antes da faxina 2026-05-28:
- 15 branches feat/* locais paralelas (somente master sobreviveu na faxina)
- 9 worktrees ativos (1 ficou + 8 deletados)
- 3 stashes esquecidos (todos deletados)
- 5 PRs com mergeStateStatus=DIRTY/CONFLICTING (nunca mergearam)
- Diretorios orfaos em .claude/worktrees/ e EasyStok.worktrees/

Commit `d030e837 docs(v1.0): AUDIT-RESULTS obsoleto-parcial + drift R1 v2.1
(TDD direto master)` ja documentava que a regra R1 v2.1 estava sendo violada
na pratica — TDD direto em master ao inves de branch+PR formal.

Master, mesmo periodo: 1172 commits em 60 dias (~20/dia). O throughput real
do projeto e alto. O overhead do sistema ETK (claim + heartbeat + complete +
worktree + branch + PR + TDD em 3 commits) nao corresponde a esse ritmo.

Diagnostico: o sistema foi desenhado para coordenacao multi-agente paralela
mas o projeto e solo (1 dev humano + 1 agente Claude por vez). Paralelismo
era prematuro. Branches acumularam sem mergear. Coordenacao virou peso morto.

## Decisao

1. **Master-first:** commit direto em master e o default. Nao ha branch
   feature por default. Nao ha worktree alem do principal. Nao ha stash.
2. **Sem PR por default:** PR so se Felipe pedir explicitamente NESTA sessao.
   Quando criada, deve mergear no mesmo dia. PR > 24h sem merge: abandonar.
3. **1 sessao Claude por vez no repo.** Multitarefa eliminada. Termina A
   antes de iniciar B.
4. **TDD opcional.** Recomendado em dominio critico (Caixa, Pedidos, Pagamentos,
   NFe) mas sem regra dura. Quality gate unico antes de commit:
   `dotnet build` + `dotnet test --filter "Category=Architecture"`.
5. **Mudanca grande = aviso, nao PR.** Threshold: > 100 LoC OU > 5 arquivos
   OU breaking change publica OU toca infra critica. Felipe decide se fatia,
   prossegue, ou cancela.
6. **Sistema ETK arquivado:**
   - docs/tasks/ inteiro movido para docs/tasks/_arquivo/2026-05-28-experimento-etk-superseded/
   - scripts/tasks/ inteiro movido para scripts/_arquivo/tasks-2026-05-28/
   - ADR-0020 marcado Superseded
7. **Handoff de sessao opcional** (era obrigatorio acima de 3 commits / 30min).
   So criar se houver decisao nao-documentada-em-ADR ou estado parcial nao-obvio
   no git log.

## Consequencias

### Positivas

- **Sem branches infinitas:** impossivel por construcao (nao existem branches feature).
- **Sem conflito "mesmos arquivos":** impossivel por construcao (1 sessao por vez).
- **Sem lixo acumulado:** sem worktree/stash/parking permitidos.
- **Throughput maximo:** zero overhead de claim/heartbeat/complete/worktree/PR.
- **CLAUDE.md ~150 linhas** (era ~330).
- **Menos token gasto** navegando estado e regras.

### Negativas / aceitas

- **Sem review pre-merge automatico:** mitigado por build + arch test obrigatorios
  + R5 (aviso de mudanca grande) + R10 (sanity check).
- **Sem rollback fácil via revert de PR:** mitigado por commits atomicos pequenos +
  git revert por SHA.
- **Sem paralelismo:** aceito — projeto e solo. Quando precisar paralelizar,
  duas maquinas com clones separados, sincronia via pull/push.
- **Auditoria depende de git log:** Conventional Commits + corpo descritivo
  fornece rastro suficiente.

### Recuperacao do que foi arquivado

Se algum item do ETK ainda valer:
- Tasks YAML estao em `docs/tasks/_arquivo/2026-05-28-experimento-etk-superseded/`
- Backlog tinha 32 ideias — pode ser fonte de planejamento futuro (sem o overhead ETK)
- Roadmap canonico segue em `docs/plan/`

## Reversao

Esta decisao sera revisada em **2026-06-27** (30 dias). Indicadores de regressao
que justificariam ADR-0023 reintroduzindo PR como gate:

1. Bugs em producao por falta de review pre-merge (frequencia > 1/semana)
2. Perda significativa de contexto entre sessoes (sem handoff)
3. Demanda real por paralelismo (multiplos contributors)

Sem esses sinais, v3.0 permanece.

## Referencias

- ADR-0020 (Superseded) — tasks ETK + TDD + multitarefa
- CLAUDE.md v3.0 — protocolo operacional desta decisao
- Faxina 2026-05-28 — sessao que zerou branches/worktrees/stashes
- docs/tasks/_arquivo/2026-05-28-experimento-etk-superseded/ — arquivos do experimento
- scripts/_arquivo/tasks-2026-05-28/ — scripts do experimento
