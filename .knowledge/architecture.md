# Arquitetura — EasyStock

## Visão de alto nível

```
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│ EasyStock.  │  │ EasyStock.  │  │ EasyStock.  │
│ Api (.NET 9)│  │ Web (MVC)   │  │ Admin (RP)  │
└──────┬──────┘  └──────┬──────┘  └──────┬──────┘
       │                │ HTTP            │ HTTP
       │                └────────┬────────┘
       ▼                         ▼
┌──────────────────────────────────────┐
│  EasyStock.Application (Use Cases)   │
│  - Ports (Input/Output)              │
│  - Services (domain integration)     │
└──────┬──────────────┬────────────┬───┘
       │              │            │
       ▼              ▼            ▼
┌─────────────┐ ┌────────────┐ ┌────────────┐
│ Infra.      │ │ Infra.     │ │ Infra.     │
│ Postgre     │ │ MongoDb    │ │ Async      │
│ (EF Core 9) │ │ (legado)   │ │ (jobs/HTTP)│
└──────┬──────┘ └────────────┘ └────────────┘
       │
       ▼
┌────────────────┐
│ EasyStock.     │
│ Domain         │
│ (POCO puro)    │
└────────────────┘
```

## Projetos

| Projeto | Tipo | Dependências | Papel |
|---|---|---|---|
| `EasyStock.Domain` | classlib | nenhuma | entities, VOs, enums, exceptions |
| `EasyStock.Application` | classlib | Domain | use cases, ports, services |
| `EasyStock.Infra.Postgre` | classlib | Application, EF Core | repos EF, DbContext, migrations |
| `EasyStock.Infra.MongoDb` | classlib | Application, MongoDB.Driver | repos Mongo (parcial) |
| `EasyStock.Infra.Async` | classlib | Application | HTTP externo (Efí, Email), jobs |
| `EasyStock.Api` | webapi | Application, Infra.* | controllers, middleware, jobs |
| `EasyStock.Web` | mvc | apenas Api via HTTP | UI MVC pro tenant |
| `EasyStock.Admin` | razor pages | apenas Api via HTTP | UI admin global |
| `EasyStock.*.Tests` | xunit | projeto-alvo | testes |

## Regra de fluxo

- **Domain não conhece nada**. Sem `using` externo além de System.
- **Application define interfaces** (`IPedidoRepository`, `IEfiPixService`). Infra implementa.
- **Infra registra DI** via `AddXxxInfra()` extension.
- **Api compõe** todos os Add* + middleware + endpoints.
- **Web/Admin não conhecem Application/Infra** — só consomem `EasyStock.Api` via `ApiClient`.

## Cross-cutting

- **Multi-tenant**: `ITenantAccessor` resolvido do JWT, usado em filtros manuais nos repos.
- **Auth**: JWT (api) + cookie (web/admin). Refresh token rotation.
- **Logging**: Serilog console + arquivo. CorrelationId via middleware.
- **Background jobs**: `IHostedService` direto, sem Hangfire/Quartz. Flags em `BackgroundJobs:*`.
- **Exception handling**: `GlobalExceptionHandler` mapeia exceptions de domínio pra HTTP status.

## Diretórios notáveis

- `casa-da-baba-mobile/pwa/` — PWA white-label vanilla JS
- `EasyStock.Api/wwwroot/pwa/` — versão servida pela API
- `scripts/` — deploy GCP, seed, utilitários
- `docs/reports/` — relatórios de análise (ANALISE-CONCORRENTES, Visão de Valor)
- `.knowledge/` — esta knowledge base (gitignored)
