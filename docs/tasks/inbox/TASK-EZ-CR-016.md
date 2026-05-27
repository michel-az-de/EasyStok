# TASK-EZ-CR-016 — Sweep comentarios ingles → pt-BR

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-20)
**Prioridade:** P3
**Esforco:** M (1 PR por sub-pasta)
**Status:** inbox

## Objetivo

Traduzir todos os comentarios em ingles para pt-BR, conforme CLAUDE.md (R) e ADR-0011.

## Estimativa

30+ comentarios em ingles, concentrados em controllers admin recentes.

## Escopo

| Arquivo | Linhas (aprox) |
|---|---|
| `AdminAdminsController.cs` | 61 |
| `AdminApkReleaseController.cs` | 11-20 (xmldoc) |
| `AdminAuditLogsController.cs` | 79 |
| `AdminBuscaGlobalController.cs` | 35-123 (varios) |
| `AdminClientesController.cs` | 21-23, 180-234 (xmldocs + inline) |

## Plano

Dividir em PRs por sub-pasta:
- PR 1: `Controllers/Admin*.cs`
- PR 2: `Controllers/` (raiz, nao-admin)
- PR 3: `Mobile/Controllers/`, `Services/`, `Observability/`
- PR 4: outros (`Configuration/`, `Authorization/`, `Middleware/`, `Http/`)

## Definicao de Pronto

- [ ] `grep -i "// (find|create|return|mask|update|delete|the )" --include='*.cs'` no `EasyStock.Api/` retorna apenas falsos positivos
- [ ] xmldocs em pt-BR
- [ ] `dotnet build` verde
- [ ] PRs mergeados sequencialmente

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-20)
- CLAUDE.md regra de pt-BR
- `docs/adr/0011-nomenclatura-pt-br-rotulagem.md`
