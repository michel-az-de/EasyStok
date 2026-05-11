# CLAUDE.md — EasyStok

Porta de entrada para qualquer agente (Claude Code, Copilot, etc.) abrindo este repo. Lido automaticamente pelo Claude Code.

## Antes de tocar codigo

**Leia, nesta ordem:**
1. `.knowledge/README.md` — single source of truth tecnica do projeto. Indexa todos os outros docs.
2. Conforme a tarefa, abra 1-2 arquivos do `.knowledge/` (mapa em `.knowledge/README.md`, secao "Como usar isso pra economizar tokens").

**Toda regra tecnica vive no `.knowledge/`** (in-repo, gitada, evolui com o codigo). Este `CLAUDE.md` so resume o critico imediato.

## Resumo critico (vale para qualquer turno)

- **Stack**: .NET 9, PostgreSQL Azure, EF Core 9, Clean Architecture estrita.
- **Solucao**: `EasyStok.sln` — 17 projetos. Pasta MAUI e `EasyStok.Mobile` (com K, sem ponto Maui).
- **Branch principal**: `master`. **Deploy**: Render (`.github/workflows/deploy-render.yml` + `render.yaml`). Azure App Service foi descomissionado. Push em master roda CI; promote a producao exige aprovacao no GitHub Environment `production`.
- **Frontends do operador**: PWA em `EasyStock.Api/wwwroot/pwa/` (fonte da verdade) + copia empacotada em `EasyStok.Mobile/Resources/Raw/pwa/`. **Merge unidirecional `PWA -> MAUI` no MESMO commit + hash SHA-256 conferido** (ver `.knowledge/dual-frontend-policy.md`).
- **Branch isolada por demanda — OBRIGATORIO**: NUNCA codar direto em `master` nem em branch alheia. **Antes de qualquer change**, crie uma branch dedicada a partir de `origin/master` com nome `tipo/escopo-curto` (ex: `fix/topbar-dropdowns`, `feat/onboarding-pix`). Se o working dir ja tem changes alheios pendentes (`git status` nao-vazio), use **worktree** (`git worktree add -b <branch> .claude/worktrees/<nome> origin/master`) — nao stash, nao mexa no estado de outra demanda.
- **Ao final de TODA demanda**: commit `tipo(escopo): desc` em PT-BR + `git push -u origin <sua-branch>` + `gh pr create --base master --head <sua-branch>` com test plan no body. **NAO mergear** — o PR fica aberto para o agente revisor decidir. Co-Author `Claude Opus 4.7 <noreply@anthropic.com>`.
- **`git status` mostrando arquivos alheios**: NAO usar `git add -A`/`git add .` — adicionar so os especificos da demanda. Se ha arquivos nao relacionados pendentes, voce esta na branch errada — saia e crie a sua via worktree.
- **Apos push tocando PWA ou casa-da-baba-mobile/apk**: workflow `build-casa-da-baba-apk.yml` dispara — aguardar e baixar APK pra `C:\rep\EasyStok\builds\app-debug.apk`.
- **Multi-tenant e RISCO MAXIMO**: `empresaId` do JWT em todo lugar; `ValidateEmpresaId` em body POST/PUT; defesa em camadas = Global Query Filter automatico (`EasyStockDbContext.ApplyTenantQueryFilters`) **+** checagem `entity.EmpresaId == command.EmpresaId` no use case; fail fast 400 se invalido.
- **NAO criar `.md` de documentacao** salvo se Felipe pedir explicitamente.
- **Estilo do Felipe**: PT-BR direto, sem floreio, sem virgula sobrando, sem travessoes. Resposta = acao + resultado.

## Atalho de roteamento

| Tarefa toca... | Abre... |
|---|---|
| EF Core / migration / VO | `.knowledge/conventions.md` + `.knowledge/do-not-do.md` |
| multi-tenant / auth | `.knowledge/conventions.md` + `.knowledge/do-not-do.md` |
| Pedido / Estoque / Caixa / Compras | `.knowledge/domain-glossary.md` + `.knowledge/current-state.md` |
| PWA ou MAUI | `.knowledge/dual-frontend-policy.md` (SEMPRE, antes de qualquer change) |
| "estado do projeto" | `.knowledge/current-state.md` + `.knowledge/recent-evolution.md` |
| deploy | `.knowledge/current-state.md` (Infra) + `render.yaml` + `.github/workflows/deploy-render.yml` |
| auditoria pessimista | `.knowledge/audit-brutal.md` (nao regenere — usa muitos tokens) |
| "o que falta" | `.knowledge/tech-debt.md` + `.knowledge/stability-roadmap.md` |
