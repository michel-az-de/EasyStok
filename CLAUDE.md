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
- **Branch principal**: `master`. **Deploy**: Render auto-deploya em merge de PR pra `master` (4 servicos: API, Web, Admin, Worker + Postgres Basic-256mb). Tem tambem workflows Azure/GCP legados em `.github/workflows/` mas nao sao o producao atual.
- **Frontends do operador**: PWA em `EasyStock.Api/wwwroot/pwa/` (fonte da verdade) + copia empacotada em `EasyStok.Mobile/Resources/Raw/pwa/`. **Merge unidirecional `PWA -> MAUI` no MESMO commit + hash SHA-256 conferido** (ver `.knowledge/dual-frontend-policy.md`).
- **Master e branch protegida** (a partir de 2026-05-07): pre-push hook em `scripts/git-hooks/pre-push` rejeita push direto. Ativar em cada clone com `git config core.hooksPath scripts/git-hooks`. Repo configurado com squash-merge only + delete branch on merge.
- **Ao final de TODA demanda**: commits em branch `feat/<escopo>` ou `fix/<escopo>` (ou usar branch ja criada pelo worktree do Claude Code) + `git push -u origin <branch>` + `gh pr create --base master`. Co-Author `Claude Opus 4.7 <noreply@anthropic.com>` em todo commit. **NUNCA mergear sozinho** — devolver controle pro Felipe com URL do PR.
- **`git status` mostrando arquivos alheios**: NAO usar `git add -A`/`git add .` — adicionar so os especificos da demanda.
- **Apos merge de PR tocando PWA ou casa-da-baba-mobile/apk**: workflow `build-casa-da-baba-apk.yml` dispara — aguardar e baixar APK pra `C:\rep\EasyStok\builds\app-debug.apk`.
- **Economia de pipeline minutes do Render** (Hobby = 500 min/mes, $5/1000 min extra): build filters configurados por servico no Render UI; auto-deploy seletivo (Worker = manual); service previews OFF; spend limit = $10/mes. Nao quebrar segregacao copiando codigo entre projetos sem necessidade.
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
