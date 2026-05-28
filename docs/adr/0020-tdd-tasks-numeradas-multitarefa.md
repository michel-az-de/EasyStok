# ADR-0020 — Tasks numeradas, TDD obrigatório, multitarefa via worktree

**Status:** SUPERSEDED por [ADR-0022](0022-master-first-trunk-based.md) em 2026-05-28
**Data:** 2026-05-24
**Resultado medido:** 3.6% de conclusão em 4 dias (2 done / 55 tasks). Branches infinitas acumularam, 5 PRs travaram em conflito, multitarefa nunca foi exercitada de fato. Ver ADR-0022 para o diagnóstico completo e a política substituta (master-first trunk-based).

## Contexto

Antes desta decisão:
- O EasyStok tinha planos por módulo (`docs/plan/`), ADRs (`docs/adr/`), handoffs (`docs/dev/sessoes/`), incidentes (`docs/dev/incidentes/`), mas **não tinha unidade de trabalho rastreável**. Trabalho era organizado por sessão Claude, não por escopo.
- `CLAUDE.md` R5 proibia paralelismo: "1 sessão ativa por vez". Boa pra evitar race conditions mas impedia ganho de velocidade quando havia trabalho independente que poderia rodar em paralelo (entities domain, fixes de testes, módulos independentes).
- `CLAUDE.md` R1 dizia "Nunca commit direto em master. Sempre branch + PR" mas `AGENTS.md` dizia "commit direto em master — não há workflow PR para development". Conflito não resolvido.
- TDD era recomendado mas não formalizado. R8 exigia "build + test antes de cada commit" mas não exigia red/green/refactor explícito.
- Trabalhar em equipe (ou com múltiplos agentes Claude) não tinha protocolo seguro.

Casa da Babá implementou um sistema multi-agente baseado em tasks YAML numeradas, locks atômicos por filesystem, worktree por task, TDD obrigatório. Funcionou na prática: 18 tasks materializadas, 12 done, zero race condition de coordenação após adoção de worktree (ADR casa-da-baba 0015).

## Decisão

### 1. Tasks numeradas como unidade de trabalho

Tasks vivem em `docs/tasks/` no padrão pasta-canonical:
- `docs/tasks/backlog/` — não claimadas
- `docs/tasks/in-progress/` — em andamento
- `docs/tasks/done/` — concluídas
- `docs/tasks/blocked/` — bloqueadas
- `docs/tasks/locks/` — locks atômicos

Numeração: **`ETK-NNNN`** sequencial monotônico. Sem prefixos por área (decisão pela simplicidade). Área vai no campo `module:` do YAML.

Cada task é YAML auto-suficiente (schema em `docs/tasks/_schema.md`):
```yaml
id: ETK-0042
title: "Implementar X"
status: backlog
priority: P1
module: nota-fiscal | caixa | mobile | core | ...
estimate_hours: 1.5
methodology: tdd | incremental | refactor
depends_on: [ETK-0041]
blocks: []
context: |
  Descrição livre do problema e por quê
acceptance_criteria: [...]
quality_gates:
  - "dotnet build EasyStok.sln -warnaserror"
  - "dotnet test --filter Architecture"
phases:                # se methodology=tdd
  red: { description, commit_template }
  green: { description, commit_template }
  refactor: { description, commit_template }
```

### 2. TDD obrigatório por default

Tasks com `methodology: tdd` exigem 3 commits separados:
- `test(ETK-NNNN): red - <descrição>` — testes falhando
- `feat(ETK-NNNN): green - <descrição>` — testes passando
- `refactor(ETK-NNNN): <descrição>` — limpeza (se houver)

Exceções (`methodology: refactor` ou `methodology: incremental`) devem ser justificadas no YAML.

Quality gates obrigatórios mínimos:
- `dotnet build EasyStok.sln --nologo` (verde, warningsaserror quando viável)
- `dotnet test --filter "FullyQualifiedName~Architecture"` (100% pass)
- Para tasks tocando domain: `dotnet test --filter "Category!=E2E"` (full regression)
- `dotnet format --verify-no-changes` clean nos arquivos novos
- Coverage delta ≥ 0 (nunca regredir)

### 3. Multitarefa via worktree obrigatório

**R5 reformulada:** *"1 sessão Claude por TASK, não por repo."*

Múltiplas sessões podem rodar em paralelo desde que:
1. Cada sessão claime uma task diferente via lock atômico
2. Cada sessão usa worktree próprio em `.claude/worktrees/wt-<task-id>/`
3. Lock contém: `task_id`, `agent`, `worktree_path`, `branch`, `claimed_at`, `expires_at`, `heartbeat_at`
4. Stale detection: lock sem heartbeat há > 6h é reclamável por coordinator

### 4. Branch flow híbrido

R1 reformulada para diferenciar:
- **Tasks formais (ETK-*)**: branch obrigatória `feat/etk-NNNN-slug` + PR + `gh pr merge --admin --squash --delete-branch`
- **Hotfix / typo / doc menor (< 1h, < 5 arquivos)**: commit direto em master ainda permitido, conforme AGENTS.md
- Decisão entre os dois: se a mudança merece YAML/ID, é formal. Senão, direto.

### 5. Issues canônicas

Sinalização de problemas durante desenvolvimento/revisão/merge vive em `docs/issues/{open,resolved}/`. Schema em `docs/issues/_schema.md`. Severity P0-blocker bloqueia merge da task associada.

Auto-detect: tasks duplicadas (mesma ID em pastas múltiplas) e locks órfãos (lock sem task em in-progress) viram issues `SYS-*` automáticas.

## Consequências

### Positivas
- **Rastreabilidade** de cada unidade de trabalho via ID estável
- **Paralelismo seguro** via worktree por task
- **TDD enforçado** via gates obrigatórios + commits separados
- **Cultura solo-dev preservada** pra trabalho pequeno (hotfix direto em master)
- **Dashboard live** mostra ROADMAP × tasks materializadas × tasks done — visibilidade real-time
- **Compatível com agentes Claude paralelos** sem race condition de coordenação

### Negativas / tradeoffs
- **Overhead** pra trabalho pequeno: agora há decisão "é task formal ou hotfix?" que antes não existia
- **Worktrees consomem disco**: cada um ~500MB. Solo dev com 4-5 ativos = 2-3GB. Aceitável em SSDs modernos.
- **Conflitos com cultura existente**: precisa atualizar CLAUDE.md (R1, R5), AGENTS.md
- **Migração**: trabalho legado não tem ID. Não vamos re-numerar histórico — só aplica daqui pra frente.

## Implementação

Esta decisão é executada pela primeira task formal do novo sistema (sem ID — bootstrap). Após bootstrap:
- 5 tasks ETK-0001 a ETK-0005 materializam etapas do ROADMAP do CLAUDE.md item 6
- ~20 tasks adicionais extraídas dos planos existentes (`docs/plan/01-dominio.md` ... `08-riscos.md`)

## Referências

- Casa da Babá ADR-0015 (worktree por task) — inspiração
- Sessões anteriores: `docs/dev/sessoes/2026-05-16-agentes-paralelos-trabalho-paralelo.md`
- Schema das tasks: `docs/tasks/_schema.md`
- Schema das issues: `docs/issues/_schema.md`
- Protocolo operacional atualizado: `CLAUDE.md` v2.1
