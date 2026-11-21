# CLAUDE.md — EasyStok (Claude Code)

**Plano vivo (D15):** atividade que edita arquivo, roda comando com efeito ou delega a agente começa com um plano em `~/.claude/planos/<sessão>.md`, que passa por banca, é relido a cada ação e vira o handoff. Regra completa na seção D15 de `ATLAS_AGENT_DISCIPLINE_V1`, projetada em `~/.claude/CLAUDE.md` e `~/.codex/AGENTS.md`.

Protocolo PR-first (ADR-0043, tiers na ADR-0055). Regras comuns a todo agente: @AGENTS.md. Aqui só o que é específico do Claude Code. Pedido explícito da sessão e segurança prevalecem.

## Repo

| Chave | Valor |
|---|---|
| Repo / trunk | `michel-az-de/EasyStok` / `master` |
| Build leve | `powershell -File scripts/poka-yoke/build-check.ps1` |
| Identidade | `user.email michel.az.de@gmail.com`; `gh` logado como `michel-az-de` |
| Labels | módulo: caixa, nfe, rotulagem, mobile, storefront, pwa, infra, web-api, domain, migrations, admin, web. Prioridade: `priority:p0..p3` |
| Tier alto | Pela natureza, nunca pelo tamanho: migração, auth, RLS, policy e processo (`CLAUDE.md`, `AGENTS.md`, ADR, workflows), `Program.cs`, Dockerfile, `*.csproj`, breaking, e feat/refactor sem spec. PR aberta até a label `aprovado`, que o agente aplica em nome do Felipe nas condições de Autorizações (ADR-0070). |
| Tier baixo | O resto: chore, docs, test, fix, perf e spec S/P de plano aprovado em `docs/plan/**`. Merge no verde. |

## Início de sessão

Com cwd na raiz e git puro (nunca `git -C`):

```
git status --short
git branch --show-current
git rev-list --count origin/master..master
git rev-list --count master..origin/master
```

Reporte em até 5 linhas: branch, ahead/behind, working tree, worktrees extras (`scripts/poka-yoke/worktree-status.ps1`). Mudança não commitada que não é da tarefa ativa: PARE e pergunte. Worktrees em `.claude/worktrees/` são esperadas. Não rode build no início: o gate do commit já builda.

## Ciclo da tarefa

1. Issue (`gh issue create`, sem esperar aprovação): título imperativo, Contexto/Escopo/Aceite com checkboxes, labels módulo + prioridade. Sem issue não há trabalho versionado.
2. Branch, stage, gate e commit conforme o AGENTS.md. Antes do commit: `git diff --cached --stat` e o fragmento `changelog.d/<issue>.md` (ADR-0055).
3. `git push -u origin HEAD`, `gh pr create --base master` com `closes #N`, `gh pr checks --watch`, `/code-review`, aceite da issue todo marcado.
4. Merge por tier: `gh pr view --json mergeable,mergeStateStatus` = `MERGEABLE`, master local em `--ff-only`, `gh pr merge --squash --delete-branch`. Conflito: `git merge origin/master`, gate de novo, push.
5. Limpeza: worktree remove + prune, branch local pelo critério da linha Limpeza do AGENTS.md. ADR se houve decisão.

Vermelho (CI falhou, review Critical, aceite aberto): PR fica aberta com os achados comentados, sem merge.
Comente na issue: plano, decisão não óbvia, premissa refutada, fechamento.

## Autorizações

Pré-autorizado, em sequência e sem pedir confirmação (Felipe, 2026-10-12): issue, push, PR, merge de tier baixo verde, deploy por `scripts/deploy/vps-deploy.sh` e `console-deploy.sh`, limpeza.
Delegado (Felipe, 2026-11-21, ADR-0070): em tier alto o agente aplica a label `aprovado` em nome do Felipe, mergeia e faz deploy sem perguntar, com CI verde, `/code-review` sem Critical feito por subagente sem o contexto da sessão autora e aceite da issue todo marcado. Antes da label, comente na PR a delegação e a prova das três condições. Faltou uma: sem label e sem merge, PR aberta com os achados comentados, sem perguntar ao Felipe.
Pare só em: segredo ou dado que só o Felipe tem; trava do Claude Code (tente uma vez; negado, entregue o comando pronto em PowerShell).
Exige GO nesta sessão: `push --force*`, `reset --hard`, rebase de história publicada, `branch -D` alheia, `revert` no master, `rm -rf` fora de artefatos, `dotnet ef database update`, segredo de produção, mudança em produção fora dos scripts de deploy, `gh release delete`, commit direto no master (hotfix: issue post-hoc com label hotfix e CI vigiado).
Nunca: `gh repo delete`.

## Fechamento

Assevere por exit code ou JSON: aceite marcado, issue fechada, PR mergeada, branches remota e local removidas, worktree removido, CI verde no master, working tree limpo.

## Leitura por área

Incidentes `docs/dev/incidentes/`, flaky `docs/dev/flaky-tests.md`, consolidação `docs/plan/consolidacao-v1/README.md`, nomenclatura ADR-0011/0018, CancellationToken ADR-0013, mobile ADR-0019. Handoff em `docs/dev/sessoes/` só com decisão não documentada ou pedido.
