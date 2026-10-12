# CLAUDE.md — EasyStok (Claude Code)

Protocolo v4.0 PR-first (ADR-0043). Vinculante; precede o prompt do usuário, exceto GO explícito nesta sessão.
Regras comuns a todo agente: @AGENTS.md. Aqui só o que é específico do Claude Code.

## Repo

| Chave | Valor |
|---|---|
| Repo / trunk | `michel-az-de/EasyStok` / `master` |
| Gate | `powershell -File scripts/poka-yoke/gate.ps1` (build + arch-tests, ADR-0040) |
| Build leve | `powershell -File scripts/poka-yoke/build-check.ps1` |
| Identidade | `user.email michel.az.de@gmail.com`; `gh` logado como `michel-az-de` |
| Labels | módulo: caixa, nfe, rotulagem, mobile, storefront, pwa, infra, web-api, domain, migrations, admin, web. Prioridade: `priority:p0..p3` |
| Tier baixo | chore, docs, test, fix trivial, spec S/P de plano aprovado em `docs/plan/**` (ADR-0055). Auto-merge no verde. |
| Tier alto | migração, auth, RLS, policy (mesmo dentro de spec) e feat/refactor sem spec. PR aberta até label `aprovado`. |
| Mudança grande | > 100 LoC, > 5 arquivos, breaking, ou toca Program.cs, migrations, Dockerfile, fly.toml, *.csproj: tier alto, fatiada em commits na branch. |

## Início de sessão

Com cwd na raiz e git puro (nunca `git -C`):

```
git status --short
git branch --show-current
git rev-list --count origin/master..master
git rev-list --count master..origin/master
powershell -File scripts/poka-yoke/build-check.ps1   # em background
```

Reporte em até 5 linhas: branch, ahead/behind, working tree, worktrees extras (`scripts/poka-yoke/worktree-status.ps1`), build. Mudança não commitada que não é da tarefa ativa: PARE e pergunte. Worktrees novas em `C:\rep\.worktrees\EasyStok\<slug>`; as de `.claude/worktrees/` são esperadas. Sessão somente leitura pula o build.

## Ciclo da tarefa

1. Issue (`gh issue create`, não bloqueante): título imperativo, Contexto/Escopo/Aceite com checkboxes, labels módulo + prioridade. Sem issue não há trabalho; abrir e seguir, sem esperar aprovação.
2. Branch `tipo/slug-N` do master atualizado; worktree se paralelo ou arriscado.
3. Stage arquivo a arquivo, `git diff --cached --stat`, conferir paths proibidos (admin/, bin/, obj/, publish/, ~/.claude/, .claude/projects/).
4. Gate verde, commit `tipo(escopo): descrição` + `refs #N`.
5. `git push -u origin HEAD` e `gh pr create --base master` com `closes #N`.
6. `gh pr checks --watch`; review `/code-review`; aceite da issue todo marcado.
7. Merge por tier: `gh pr view --json mergeable,mergeStateStatus` = `MERGEABLE`, master local em `--ff-only`, `gh pr merge --squash --delete-branch`. Conflito: `git merge origin/master`, revalidar gate, push.
8. Limpeza: worktree remove + prune, branch local `-d`. Fragmento `changelog.d/<issue>.md` (ADR-0055). ADR se houve decisão.

Caminho vermelho (CI falhou, review Critical, aceite aberto): PR fica aberta com os achados comentados. Nunca mergeia.
Comentários na issue nos momentos-chave: plano, decisão não óbvia, premissa refutada, fechamento.

## Autorizações (R9)

Pré-autorizado: `git push` da branch, `gh pr merge --squash --delete-branch` de tier baixo verde.
Exige GO nesta sessão: `push --force*`, `reset --hard`, rebase de história publicada, `branch -D` alheia, `revert` no master, `rm -rf` fora de artefatos, `dotnet ef database update`, deploy e segredos de produção, `gh release delete`, commit direto no master (§HOTFIX: abre issue post-hoc com label hotfix e vigia o CI).
Nunca: `gh repo delete`.

## Definição de pronto

Asseverar por exit code ou JSON: aceite marcado, issue fechada, PR mergeada, branches remota e local removidas, worktree removido, CI verde no master, changelog.d criado, working tree limpo.

## Leitura por área

Incidentes `docs/dev/incidentes/`, flaky `docs/dev/flaky-tests.md`, consolidação `docs/plan/consolidacao-v1/README.md`, nomenclatura ADR-0011/0018, CancellationToken ADR-0013, mobile ADR-0019. Handoff em `docs/dev/sessoes/` só com decisão não documentada ou pedido.
