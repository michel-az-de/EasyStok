# EasyStok — Quick Reference

Pagina unica para sessoes Claude Code. Complementa (NAO substitui) CLAUDE.md.

---

## 1. Boot da sessao (copia e cola)

```powershell
git -C C:\easy\EasyStok status --short
git -C C:\easy\EasyStok log master --oneline -5
git -C C:\easy\EasyStok rev-list --count origin/master..master
git -C C:\easy\EasyStok rev-list --count master..origin/master
git -C C:\easy\EasyStok worktree list
git -C C:\easy\EasyStok branch --show-current
gh auth status
dotnet build C:\easy\EasyStok\EasyStok.sln --nologo --verbosity quiet
```

Reportar em 6 linhas (template em CLAUDE.md §0).

---

## 2. Onde fica o que

| Procurando            | Caminho                                  |
|-----------------------|------------------------------------------|
| Protocolo operacional | CLAUDE.md (raiz)                         |
| ADRs (decisoes)       | docs/adr/                                |
| Sessoes recentes      | docs/dev/sessoes/ (5d ou menos)          |
| Sessoes antigas       | docs/dev/sessoes/_arquivo/               |
| Incidentes            | docs/dev/incidentes/                     |
| Flaky tests           | docs/dev/flaky-tests.md                  |
| Plano nota-fiscal     | docs/plan/nota-fiscal/00-README.md       |
| Plano P-02 rotulagem  | docs/plan/p-02-rotulagem-nutricional.md  |
| Repo VIVO             | C:\easy\EasyStok                         |
| Worktrees padrao      | C:\easy\EasyStok\.claude\worktrees\wt-*  |

---

## 3. Tarefa → comando padrao

### Criar worktree de feature (prefixo wt- obrigatorio — R4)
```powershell
git -C C:\easy\EasyStok worktree add C:\easy\EasyStok\.claude\worktrees\wt-<nome> -b <branch> origin/master
```

### Stage seguro (NUNCA git add . ou -A — R2)
```powershell
git add <path/especifico>
git diff --cached --stat
# validar visualmente que so aparecem arquivos do escopo
```

### Validar antes de commitar (R8)
```powershell
dotnet build C:\easy\EasyStok\EasyStok.sln --nologo
dotnet test --filter "FullyQualifiedName~Architecture"
```

### Mensagem de commit (Conventional Commits — R3)
```
tipo(escopo): descricao imperativa

# tipos validos: feat, fix, refactor, docs, chore, test, perf, ci, build
# proibidos: wip, snapshot, checkpoint, fix this, temp, tmp, asdf
```

### Merge de PR (apos aprovacao — R1)
```powershell
gh pr merge <num> --admin --squash --delete-branch
```
**ATENCAO**: ver memoria `gh-pr-merge-delete-branch-gotcha` — separar passos
em casos de mergeable=UNKNOWN.

### Limpar worktree apos PR mergeada
```powershell
git -C C:\easy\EasyStok worktree remove C:\easy\EasyStok\.claude\worktrees\wt-<nome>
git -C C:\easy\EasyStok branch -d <branch>   # -d (nao -D) so deleta se mergeada
```

---

## 4. Comandos que exigem autorizacao explicita NESTA sessao (R9)

Felipe precisa dizer "OK / vai / executa / confirma / GO / autorizado":
- Remove-Item -Force, rm -rf
- git push (qualquer)
- git push --force, --force-with-lease
- git reset --hard, git revert, git rebase
- git branch -D, git stash drop/clear
- dotnet ef database update
- fly deploy, fly secrets, fly volumes destroy
- gh pr close --delete-branch, gh pr merge, gh release delete

Inferir de mensagem anterior NAO conta.

---

## 5. Sinais de alerta (PARAR e perguntar — R10, R15)

| Sintoma                                  | Acao                                |
|------------------------------------------|-------------------------------------|
| Premissa do prompt nao bate com medicao  | Reportar, nao reconciliar           |
| Worktree em path diferente de .claude/   | Confirmar antes de criar/usar       |
| Branch sem prefixo wt- num worktree      | Quebra R4 — confirmar               |
| Estender assinatura publica              | Atualizar TODOS call-sites (R7)     |
| Mais de 3 commits ou 30+ min na sessao   | Handoff em docs/dev/sessoes/ (R14)  |
| Build vermelho                           | Nao commitar — investigar           |
| Mergeable=UNKNOWN ou DIRTY               | Separar passos do merge             |
| Working tree de outra sessao             | NAO TOCAR (R6)                      |

---

## 6. Fim de sessao (R14 — 3+ commits OU 30+ min)

```powershell
git status --short
git log master..HEAD --oneline
git diff HEAD --stat
dotnet build
```

Criar `docs/dev/sessoes/YYYY-MM-DD-HHMM-<tema>.md` com template do CLAUDE.md §4.

Cleanup:
- Worktrees nao-mais-usados: `git worktree remove`
- Branches mergeadas: `git branch -d`
- Stashes nao-relevantes: revisar (drop exige R9)

---

## 7. Atalhos uteis

| Para                                       | Comando                                                       |
|--------------------------------------------|---------------------------------------------------------------|
| Ver PRs abertas                            | `gh pr list --state open`                                     |
| Ver PRs mergeadas recentes                 | `gh pr list --state merged --limit 10`                        |
| Estado de mergeable da PR                  | `gh pr view <num> --json mergeable,mergeStateStatus`          |
| Branches locais ordenadas por atividade    | `git for-each-ref --sort=-committerdate refs/heads/`          |
| Branches remotas idem                      | `git for-each-ref --sort=-committerdate refs/remotes/origin/` |
| Worktrees orfaos (em disco mas nao no git) | comparar `Get-ChildItem .claude\worktrees\` vs `git worktree list` |
| Limpar bin/obj de um worktree              | `Get-ChildItem <wt> -Recurse -Directory \| ? Name -in 'bin','obj' \| Remove-Item -Recurse -Force` |

---

## 8. Memorias relevantes (MEMORY.md)

| Tema                            | Arquivo                                  |
|---------------------------------|------------------------------------------|
| Repo VIVO vs STALE              | project_repo_clones_topology.md          |
| Deploy fly + migrations gotcha  | deploy-and-migrations-gotcha.md          |
| Acesso ao Postgres prod         | prod-db-access.md                        |
| WSL + git worktree gotcha       | wsl-git-worktree-gotcha.md               |
| gh pr merge --delete-branch     | gh-pr-merge-delete-branch-gotcha.md      |
| Confirmacao vs hesitacao        | feedback_confirmacao_vs_hesitacao.md     |
| PWA Casa da Baba (3 memorias)   | project_pwa_*.md                         |
