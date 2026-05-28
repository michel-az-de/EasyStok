# EasyStock API — OpenAPI/Swagger

Especificação OpenAPI 3.0 da EasyStock.Api versionada no repo.

- **openapi.json** — documento gerado automaticamente pela [`scripts/export-openapi.ps1`](../../scripts/export-openapi.ps1).
- Swagger UI ao vivo: `https://<host>/swagger` (Development/Staging por default; controlado por `Swagger:EnableInProduction`).
- Definição do documento: [`EasyStock.Api/Configuration/SwaggerDocumentConfiguration.cs`](../../EasyStock.Api/Configuration/SwaggerDocumentConfiguration.cs).

## Como regenerar

```powershell
powershell -ExecutionPolicy Bypass -File scripts/export-openapi.ps1
```

O script:
1. Restaura a tool local `Swashbuckle.AspNetCore.Cli` (manifest em [`.config/dotnet-tools.json`](../../.config/dotnet-tools.json)).
2. Builda `EasyStock.Api` em Debug.
3. Define env vars fake (`OPENAPI_EXPORT=true`, JWT/DB stubs) e roda `dotnet swagger tofile`.
4. Salva `docs/api/openapi.json`.

`OPENAPI_EXPORT=true` instrui o `Program.cs` a sair antes de `app.Run()` — assim o Swashbuckle CLI captura o `IServiceProvider` mas o host nunca inicia os hosted services (`DispatcherLoopHostedService` etc.), que dependem de DI Postgres-only.

## Conteúdo (snapshot atual)

- ~446 endpoints
- ~124 tags (Core, Storefront, Mobile, Admin*, diagnósticos, webhooks)
- ~310 schemas (commands, DTOs, response models)
- Examples em 11 schemas (`CadastrarProduto`, `Login`, `SolicitarOtp`, `Checkout`, `PairDevice`, `SyncPush`, etc.)

## Validação rápida

```powershell
$j = Get-Content docs/api/openapi.json -Raw | ConvertFrom-Json
$j.paths.PSObject.Properties.Name.Count  # paths
$j.tags.Count                              # tags
```
