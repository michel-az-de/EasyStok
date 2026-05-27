# TASK-EZ-CR-009 — Extrair Program.cs em extension methods

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-11)
**Prioridade:** P2
**Esforco:** M
**Status:** inbox

## Objetivo

Reduzir `Program.cs` de 943 linhas para ~300 linhas, extraindo blocos em extension methods organizados em pasta `ProgramExtensions/`.

## Escopo

- [EasyStock.Api/Program.cs](../../../EasyStock.Api/Program.cs) (943 linhas no master)
- Nova pasta `EasyStock.Api/ProgramExtensions/`:
  - `StartupSecurity.cs` — JWT validation, CORS, rate limit, security headers
  - `StartupMigrations.cs` — EF migrations + seed bootstrap
  - `StartupBanner.cs` — log ASCII de boot
  - `StartupHardening.cs` — checagem de chaves perigosas em prod (`postgres`, `cdb-dev-key`, etc.)
  - `StartupHealthChecks.cs` — registros de health checks
  - `StartupProvider.cs` — auto-detect Postgres/SQLite/Mongo + fail-fast prod

## Plano

1. Identificar blocos coesos no Program.cs
2. Criar extension method `IServiceCollection.AddX(...)` ou `WebApplication.UseX(...)` para cada bloco
3. Mover codigo, validar build verde a cada passo
4. Program.cs final so com:
   - `var builder = WebApplication.CreateBuilder(args);`
   - `builder.AddX().AddY().AddZ();` (Configuration + DI)
   - `var app = builder.Build();`
   - `app.UseX().UseY();` (Middleware pipeline)
   - `app.Run();`

## Definicao de Pronto

- [ ] Program.cs com <=300 linhas
- [ ] 5-7 extension methods em `ProgramExtensions/`, cada um <200 linhas
- [ ] `dotnet build` verde
- [ ] Sem mudanca funcional (smoke test em dev)
- [ ] Cold start similar ou melhor (medir antes/depois)
- [ ] PR mergeado

## Riscos

- Ordem de middleware e critica — manter sequencia exata
- Algumas chamadas dependem de `builder.Configuration` direto — passar como parametro

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-11)
