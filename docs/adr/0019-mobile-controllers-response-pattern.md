# ADR-0019 — Mobile Controllers: ActionResult<T> em vez de ApiResponse<T>

**Status:** Aceito  
**Data:** 2026-05-18

## Contexto

O EasyStock possui duas superfícies HTTP distintas:

- **API principal** (`/api/*`) — consumida pelo frontend web (React/Next.js) e integrações ERP. Usa `EasyStockControllerBase` com os helpers `DataOk()`, `DataBadRequest()`, `DataNotFound()`, `DataPaged()`, que envolvem todas as respostas em `ApiResponse<T>` com campos `success`, `data`, `message`, `pagination`.
- **API mobile** (`/api/mobile/*`) — consumida exclusivamente pelo PWA Casa da Baba (offline-first). Usa `ControllerBase` ou `MobileManagementControllerBase` diretamente, retornando `ActionResult<T>` / `Ok()` / `Unauthorized()` sem envelope.

## Decisão

**As mobile controllers não usarão `ApiResponse<T>` nem `EasyStockControllerBase`.**

Ambas as superfícies coexistem intencionalmente. Cada uma tem seu contrato, seus consumidores e suas garantias de versão separados.

## Motivação

1. **Contrato tipado no Swagger** — `ActionResult<T>` permite que o Swagger gere schemas exatos para o PWA sem o wrapper genérico, facilitando geração de clientes TypeScript.
2. **Payload mínimo offline** — O PWA sincroniza em redes lentas/instáveis. Remover o envelope `ApiResponse<T>` reduz payload e simplifica o parser de sync.
3. **Separação de superfícies** — A API mobile tem ciclo de vida independente (versionamento, autenticação por `X-Mobile-Api-Key`, idempotência de mutations). Misturar os helpers do web criaria acoplamento desnecessário.
4. **Consistência interna** — Todos os 22 mobile controllers já seguem esse padrão. Mudar quebraria o PWA sem ganho.

## Consequências

- Novos mobile controllers devem herdar de `ControllerBase` (ou `MobileManagementControllerBase` para os de gestão) e usar `Ok()`, `BadRequest()`, `Unauthorized()`, `ActionResult<T>`.
- **Nunca** herdar `EasyStockControllerBase` em controllers mobile.
- Qualquer endpoint mobile que precise de paginação deve definir seu próprio envelope de resposta (ex: `SyncPullResponse`) em vez de usar `DataPaged()`.
- Revisores de código devem rejeitar PRs que introduzam `DataOk()` / `ApiResponse<T>` em controllers sob `EasyStock.Api.Mobile`.
