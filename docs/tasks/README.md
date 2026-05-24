# Tasks EasyStok — protocolo canônico

> Decisão arquitetural: ver [ADR-0020](../adr/0020-tdd-tasks-numeradas-multitarefa.md).

Sistema de unidades de trabalho rastreáveis com ID estável (`ETK-NNNN`), TDD obrigatório, multitarefa segura via worktree.

## Onboarding em 5 minutos

```bash
# 1. Sincronizar
cd C:/easy/EasyStok
git checkout master && git pull

# 2. Validar estado
./scripts/tasks/validate.sh

# 3. Ler última sessão
ls -t docs/dev/sessoes/ | head -1 | xargs -I{} cat docs/dev/sessoes/{}

# 4. Escolher task no backlog
cat docs/tasks/_index.yaml | grep -A 4 "status: backlog"

# 5. Ler task escolhida
cat docs/tasks/backlog/ETK-NNNN-slug.yaml

# 6. Claim atômico — cria worktree + branch + lock
./scripts/tasks/claim.sh ETK-NNNN

# 7. Ir pro worktree e trabalhar (TDD!)
cd .claude/worktrees/wt-etk-NNNN/

# 8. Heartbeat a cada 20min
./scripts/tasks/heartbeat.sh ETK-NNNN

# 9. Ao terminar, rodar quality gates
dotnet build EasyStok.sln -warnaserror
dotnet test --filter "Category!=E2E"
dotnet format --verify-no-changes

# 10. Complete + cleanup
./scripts/tasks/complete.sh ETK-NNNN
```

## Estrutura

```
docs/tasks/
├── README.md              ← este arquivo
├── _schema.md             ← schema YAML detalhado
├── _index.yaml            ← derivado (regen-index.sh)
├── backlog/               ← não claimadas
├── in-progress/           ← em andamento (1 por agente)
├── done/                  ← concluídas
├── blocked/               ← bloqueadas (com motivo no YAML)
└── locks/                 ← locks atômicos *.lock
```

**Pasta é canonical.** `_index.yaml` é derivado; nunca editar manualmente.

## Princípios

| # | Princípio | Por quê |
|---|---|---|
| 1 | Pasta canonical | `_index.yaml` é derivado de `tasks/*/*.yaml`. Re-rode `regen-index.sh` após mudança. |
| 2 | Lock por arquivo | `locks/ETK-NNNN.lock` impede 2 agentes na mesma task. |
| 3 | Worktree por task | Cada task ativa roda em `.claude/worktrees/wt-etk-NNNN/`. Isolamento de HEAD/bin/obj. |
| 4 | TDD obrigatório | Tasks com `methodology: tdd` exigem 3 commits separados (red → green → refactor). |
| 5 | Quality gates bloqueiam done | Build + tests + format limpo nos arquivos novos. |
| 6 | Handoff obrigatório | Toda task ETK-* termina com handoff em `docs/dev/sessoes/`. |

## Branch flow

**Tasks formais (ETK-*)**: branch `feat/etk-NNNN-slug` + PR + `gh pr merge --admin --squash --delete-branch`.

**Hotfix / typo / doc menor** (< 1h, < 5 arquivos sem teste novo): commit direto em master ainda OK conforme AGENTS.md.

Critério: se merece YAML/ID, é formal.

## Worktree workflow

```bash
# claim.sh faz tudo atomicamente:
./scripts/tasks/claim.sh ETK-0042
# Cria:
#   - branch feat/etk-0042-<slug> a partir de origin/master
#   - worktree em .claude/worktrees/wt-etk-0042/
#   - lock em docs/tasks/locks/ETK-0042.lock
#   - move task de backlog/ pra in-progress/
# Commit: "claim(ETK-0042): <agent>"

# Trabalhar no worktree (NUNCA no master direto pra tasks formais):
cd .claude/worktrees/wt-etk-0042/

# heartbeat a cada ~20min mantém o lock vivo:
./scripts/tasks/heartbeat.sh ETK-0042

# Ao terminar:
./scripts/tasks/complete.sh ETK-0042
# Roda gates, move pra done/, remove lock, deleta worktree, abre PR.
```

## TDD lifecycle

Para tasks `methodology: tdd`:

```bash
# RED — escreve testes que falham
git commit -m "test(ETK-NNNN): red - <descrição>"

# GREEN — implementação mínima pra passar
git commit -m "feat(ETK-NNNN): green - <descrição>"

# REFACTOR — limpar mantendo testes verdes (opcional mas recomendado)
git commit -m "refactor(ETK-NNNN): <descrição>"
```

Verificável via `complete.sh` que valida o template das mensagens.

## Stale detection

- Lock sem heartbeat há > 6h: pode ser reclamado por coordinator via `validate.sh --fix`
- Task em in-progress sem lock: SYS-ORPHAN-* issue automática
- Task em pastas múltiplas: SYS-DUP-* issue automática (P0)

## Quando algo dá errado

| Sintoma | Ação |
|---|---|
| `validate.sh` falha | Pare. Não claim. Avise. |
| Lock expirou | `./scripts/tasks/heartbeat.sh ETK-NNNN --extend 1h` ou libera+re-claim |
| Outro agente segura lock > 6h sem heartbeat | Coordinator: `./scripts/tasks/reclaim.sh ETK-NNNN` |
| Conflito em `_index.yaml` | Derivado. Apague + `regen-index.sh` |
| Worktree corrompido | `git worktree remove --force .claude/worktrees/wt-etk-NNNN/` + re-claim |

## Dashboard

Visualização ao vivo em **http://localhost:4321/** (servidor casa-da-baba multi-projeto). Painel "EasyStok — planejamento e gestão" mostra ROADMAP × tasks materializadas.
