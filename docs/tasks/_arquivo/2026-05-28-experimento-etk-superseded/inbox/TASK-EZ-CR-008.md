# TASK-EZ-CR-008 — Mover AdminSeedController para IDbSeedService

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-10)
**Prioridade:** P2
**Esforco:** M
**Status:** inbox

## Objetivo

Extrair logica de seed do `AdminSeedController` (483 linhas) para um service `IDbSeedService` na camada Application, consolidando com as classes ja existentes em `EasyStock.Api/Data/Tenants/*Seed.cs`.

## Escopo

- [EasyStock.Api/Controllers/AdminSeedController.cs](../../../EasyStock.Api/Controllers/AdminSeedController.cs) (483 linhas)
- `EasyStock.Api/Data/Tenants/*Seed.cs` (ja existem)
- Novo `EasyStock.Application/Services/IDbSeedService.cs` (interface)
- Novo `EasyStock.Api/Services/DbSeedService.cs` (impl na Api ou Infra)

## Plano

1. Mapear os metodos do controller para casos de uso (ex.: `SeedCasaDaBaba`, `SeedCantinaMauricio`, `ResetTenant`, ...)
2. Criar `IDbSeedService` com 1 metodo por caso de uso
3. Controller vira fino (so chama `_seedService.SeedTenantAsync(name, ct)`)
4. Consolidar com classes `*Seed.cs` ja existentes em `Data/Tenants/`

## Definicao de Pronto

- [ ] `AdminSeedController` com <100 linhas
- [ ] `IDbSeedService` na Application
- [ ] Impl em `EasyStock.Api.Services.DbSeedService` (ou `EasyStock.Infra.Postgre.Services.DbSeedService` se preferir)
- [ ] Tests unit/integration para cada metodo de seed
- [ ] `dotnet build` verde + Architecture tests passam
- [ ] Documentacao atualizada (CLAUDE.md item 5 menciona seeds)

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-10)
