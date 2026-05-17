# AGENTS.md — EasyStok

Porta de entrada para qualquer agente (Codex, Copilot, etc.) abrindo este repo. Lido automaticamente pelo Codex.

## Antes de tocar codigo

**Leia, nesta ordem:**
1. `.knowledge/README.md` — single source of truth tecnica do projeto. Indexa todos os outros docs.
2. Conforme a tarefa, abra 1-2 arquivos do `.knowledge/` (mapa em `.knowledge/README.md`, secao "Como usar isso pra economizar tokens").

**Toda regra tecnica vive no `.knowledge/`** (in-repo, gitada, evolui com o codigo). Este `AGENTS.md` so resume o critico imediato.

## Resumo critico (vale para qualquer turno)

- **Stack**: .NET 9, PostgreSQL, EF Core 9, Clean Architecture estrita.
- **Solucao**: `EasyStok.sln` — 17 projetos. Pasta MAUI e `EasyStok.Mobile` (com K, sem ponto Maui).
- **Branch principal**: `master`. **Deploy**: Fly.io via `fly.toml` (operacional). Render foi planejado (PR #128 open) mas bloqueado por billing CI desde 2026-05-11. NAO Azure.
- **Frontends do operador**: PWA em `EasyStock.Api/wwwroot/pwa/` (fonte da verdade) + copia empacotada em `EasyStok.Mobile/Resources/Raw/pwa/`. **Merge unidirecional `PWA -> MAUI` no MESMO commit + hash SHA-256 conferido** (ver `.knowledge/dual-frontend-policy.md`).
- **Convencao de branch (Felipe, solo dev)**: commit direto em `master` — nao ha workflow PR para development. Se a tarefa for grande ou houver changes alheios pendentes (`git status` nao-vazio), use **worktree** (`git worktree add -b <branch> .Codex/worktrees/<nome> origin/master`) para isolar; resolvido o trabalho, faz merge fast-forward em master e deleta o worktree.
- **Ao final de TODA demanda**: commit `tipo(escopo): desc` em PT-BR direto em `master` + `git push origin master`. CI em build-casa-da-baba-apk.yml (e outros workflows) esta bloqueado por billing GitHub Actions desde 2026-05-11 — deploy fly.io eh manual via `fly deploy`. Co-Author `Codex Opus 4.7 <noreply@anthropic.com>` (ou `Claude Opus 4.7 <noreply@anthropic.com>` para sessoes Claude Code).
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
| deploy | `.knowledge/current-state.md` (Infra) + `.knowledge/gcp-deploy.md` se for migracao |
| auditoria pessimista | `.knowledge/audit-brutal.md` (nao regenere — usa muitos tokens) |
| "o que falta" | `.knowledge/tech-debt.md` + `.knowledge/stability-roadmap.md` |
