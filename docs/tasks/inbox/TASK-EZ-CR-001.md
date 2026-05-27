# TASK-EZ-CR-001 — Remover BadRequest(ex.Message) que vaza excecoes (137 lugares)

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-1)
**Prioridade:** P0
**Esforco:** M (5 PRs incrementais por subdominio)
**Status:** inbox

## Objetivo

Eliminar o padrao `try { ... } catch (Exception ex) { return BadRequest(ex.Message); }` em 137 ocorrencias distribuidas em 37 controllers, deixando o `GlobalExceptionHandler` (que ja existe e mapeia ~12 tipos de excecao para codigos pt-BR) fazer seu trabalho.

## Escopo

Dividir em 5 PRs por subdominio para revisao manejavel:

| PR | Subdominio | Top arquivos |
|---|---|---|
| 1 | Admin | AdminTicketsController (13), AdminFaturasController, AdminClientesController (8), AdminEmpresaPreviewController, AdminSeedController, AdminSlaController, AdminTenantsController, AdminUsuariosTenantController, AdminDiagnosticoController |
| 2 | Business | ConfiguracaoFiscalController (11), ContasAReceberController (9), ContasAPagarController (8), CategoriasFinanceirasController, CentrosCustoController, EtiquetaTemplatesController, FaqAdminController, NotasFiscaisController, ProdutoController, TicketsController, FinanceiroController, FaqController, HelpdeskController |
| 3 | Mobile | (verificar Mobile/Controllers/ — agente nao encontrou ofensores; pode pular se confirmar 0) |
| 4 | Webhooks | WebhookGatewayController, WebhookPixController, WebhookFocusNFeController |
| 5 | Diagnostico + Storefront + Auth | DiagnosticoInfraController, DiagnosticoLogsController, AuthController (1), Public/LeadsPublicosController, Storefront/AgendamentoController, Storefront/AvaliacaoController |

**Out of scope:**
- Reescrever logica de negocio — so substituir o padrao de catch
- Mexer no `GlobalExceptionHandler` (esta correto)
- Webhooks que precisem de retorno 200 sempre — manter try/catch mas sem `ex.Message`

## Padrao de fix

```csharp
// ANTES
try {
    return Ok(await useCase.ExecuteAsync(input));
} catch (Exception ex) {
    return BadRequest(ex.Message);  // vaza internals
}

// DEPOIS — caso 1: deixar GlobalExceptionHandler agir
return Ok(await useCase.ExecuteAsync(input, ct));

// DEPOIS — caso 2 (webhooks): log + resposta generica
try {
    return Ok(await useCase.ExecuteAsync(input, ct));
} catch (Exception ex) {
    _logger.LogError(ex, "Falha no webhook {Webhook}", nameof(WebhookX));
    return Problem(detail: "Falha ao processar webhook", statusCode: 500);
}
```

## Definicao de Pronto (DoD)

- [ ] 5 PRs mergeados (1 por subdominio)
- [ ] grep `BadRequest\(ex\.Message\)|BadRequest\(new \{ erro = ex\.Message\)` retorna 0 em `EasyStock.Api/Controllers/`
- [ ] `dotnet build EasyStok.sln --nologo` verde
- [ ] `dotnet test --filter "FullyQualifiedName~Architecture"` passa
- [ ] Tests existentes nao regressam (Application.Tests, Api.UnitTests)
- [ ] Pelo menos 3 integration tests novos validam que erros de dominio retornam codigo correto (`UseCaseValidationException` → 400 com `VALIDATION_ERROR`)

## Testes esperados (TDD)

- IntegrationTest: `GivenInvalidInput_WhenPost_Then400WithGlobalErrorEnvelope`
- IntegrationTest: `GivenDomainException_WhenPost_Then409Conflict`
- IntegrationTest: `GivenInternalError_WhenPost_Then500WithGenericMessage` (sem vazar `ex.Message`)

## Riscos

- Tests de controller que assertam `ex.Message` em response body vao quebrar — atualizar para assertar codigo `VALIDATION_ERROR`, `BUSINESS_RULE_VIOLATION`, etc.
- Webhooks com idempotencia podem exigir custom catch — caso a caso

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-1)
- `EasyStock.Api/Observability/GlobalExceptionHandler.cs` (handler ja correto)
- `EasyStock.Domain.Exceptions.*` (tipos mapeados)
