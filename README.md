# EasyStok

SaaS multi-tenant de gestão de estoque/foodservice em **.NET 9 + PostgreSQL**, com PWA mobile (Casa da Babá em produção real) e MAUI Android (produto SaaS em maturação).

## Para agentes / desenvolvedores entrando agora

Leia, nesta ordem:

1. **[`CLAUDE.md`](CLAUDE.md)** — porta de entrada com regras de trabalho e atalhos críticos.
2. **[`.knowledge/README.md`](.knowledge/README.md)** — single source of truth técnica do projeto. Indexa todos os outros docs do `.knowledge/`.
3. **Tarefa específica?** Use o roteiro do `.knowledge/README.md` (cenários 1–5) pra abrir só os arquivos relevantes.

## Stack rápida

- **Backend**: .NET 9, EF Core 9, PostgreSQL Azure, Clean Architecture estrita.
- **Frontends**: `EasyStock.Web` (MVC + Alpine + Tailwind, painel lojista), `EasyStock.Admin` (back-office), `EasyStok.Mobile` (MAUI Android, com K), PWA em `EasyStock.Api/wwwroot/pwa/`.
- **Notificações**: Outbox pattern + `EasyStock.Worker` despachando Email/SMS/WhatsApp/InApp.
- **Auth**: JWT 8h + refresh 30d, biometria mobile, multi-tenant Empresa→Loja→Usuario→Perfil.
- **Deploy**: Azure App Service via `.github/workflows/deploy-azure.yml` (auto-deploy em push pra `master`/`main`).

## Comandos essenciais

```bash
dotnet build EasyStok.sln
dotnet test EasyStok.sln

# Run local
dotnet run --project EasyStock.Api      # API + PWA em /pwa/
dotnet run --project EasyStock.Web      # Painel lojista (MVC)
dotnet run --project EasyStock.Admin    # Back-office (Razor Pages)

# Migrations
dotnet ef migrations add NomeMigration \
  --project EasyStock.Infra.Postgre \
  --startup-project EasyStock.Api

dotnet ef database update \
  --project EasyStock.Infra.Postgre \
  --startup-project EasyStock.Api
```

Ver `.knowledge/quick-reference.md` para a lista completa.

## Estrutura

Categorias da `EasyStok.sln`:

- **UI**: `EasyStock.Web`, `EasyStock.Admin`, `EasyStok.Mobile`
- **Core**: `EasyStock.Domain`, `EasyStock.Application`
- **Infra**: `EasyStock.Infra.Postgre` (migrations assembly), `EasyStock.Infra.MongoDb` (descartada como transacional — ADR 0001), `EasyStock.Infra.Notifications`, `EasyStock.Infra.Async`
- **Tests**: Domain.Tests, Application.Tests, Api.UnitTests, Api.IntegrationTests (Testcontainers), Infra.Postgre.IntegrationTests, ArchitectureTests
- **Outros**: `EasyStock.Api`, `EasyStock.Worker`, `EasyStock.Benchmarks`

Detalhes em `.knowledge/architecture.md`.

## Para contribuir

- Convenções: `.knowledge/conventions.md`
- O que NÃO fazer: `.knowledge/do-not-do.md`
- Tech debt: `.knowledge/tech-debt.md`
- Roadmap de estabilização: `.knowledge/stability-roadmap.md`
- Política PWA + MAUI: `.knowledge/dual-frontend-policy.md` — **leia antes de tocar UI do operador**
