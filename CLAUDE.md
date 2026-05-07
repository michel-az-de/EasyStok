# CLAUDE.md — EasyStok

Este arquivo e lido automaticamente pelo Claude Code ao abrir o projeto. Ele garante que **toda conversa neste repositorio carregue a skill de dev consolidada**, mesmo se a auto-memoria do harness nao estiver disponivel.

## Leitura obrigatoria antes de codar

A skill completa de dev do EasyStok vive em:
**`C:\Users\f.michel.de.azevedo\.claude\projects\C--rep-EasyStok\memory\MEMORY.md`**

Esse arquivo e auto-contido e cobre:
1. O que e o EasyStok (stack, solucao, funcionalidades ja entregues)
2. Regras de trabalho com o Felipe (commit + push, APK, PT-BR, etc)
3. Padroes arquiteturais aceitos (CQRS-lite, VOs, Outbox, idempotency)
4. Armadilhas ja superadas — NAO repetir (EF, multi-tenant, auth, DTO, CI, mobile, PWA+MAUI, performance, UX)
5. Checklist por area antes de codar
6. Indice de aprofundamento opcional (`lessons_learned.md`, `dev_playbook.md`, `project_structure.md` etc)

Se voce esta lendo este `CLAUDE.md` mas nao recebeu o conteudo do `MEMORY.md` injetado automaticamente, **leia ele explicitamente com a tool Read antes de tocar qualquer codigo**.

## Atalhos de contexto (resumo critico que vale para qualquer turno)

- **Stack**: .NET 9, PostgreSQL Azure, EF Core 9. Branch `master`. Render auto-deploy via push.
- **Solucao**: `EasyStok.sln` com `EasyStock.Domain`, `EasyStock.Application`, `EasyStock.Infra.Postgre` (migrations), `EasyStock.Infra.Notifications` (Outbox), `EasyStock.Api`, `EasyStock.Admin` (back-office), `EasyStock.Web` (lojista MVC), `EasyStok.Mobile` (MAUI Android com K, sem ponto Maui), `EasyStock.Worker`.
- **Frontends do operador**: PWA em `EasyStock.Api/wwwroot/pwa/` e copia em `EasyStok.Mobile/Resources/Raw/pwa/`. Merge unidirecional `PWA -> MAUI` no MESMO commit + hash SHA-256.
- **Ao final de TODA demanda**: commit `tipo(escopo): desc` em PT-BR + `git push origin master`. Sem perguntar.
- **Apos push que toca PWA**: aguardar workflow APK + baixar pra `C:\rep\EasyStok\builds\app-debug.apk`.
- **Multi-tenant e RISCO MAXIMO**: `empresaId` do JWT em todo lugar; `ValidateEmpresaId` em body POST/PUT; sub-recurso valida pertencimento; fail fast 400 se invalido.
- **Nao criar arquivos `.md`** salvo se Felipe pedir explicitamente.

## Quando tarefa for complexa, leia tambem

- `lessons_learned.md` (na pasta de memoria) — cada armadilha com hash do commit do fix
- `dev_playbook.md` — fluxo completo, checklists expandidos
- `project_structure.md` — mapa detalhado dos 50 controllers e 57 entidades
- `.knowledge/dual-frontend-policy.md` (no proprio repo) — politica PWA+MAUI canonica
