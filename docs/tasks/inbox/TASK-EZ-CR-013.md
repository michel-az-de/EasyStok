# TASK-EZ-CR-013 — Auditar 46 catch (Exception) em controllers

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-17)
**Prioridade:** P2
**Esforco:** P
**Status:** inbox

## Objetivo

Auditar cada um dos 46 `catch (Exception)` em 13 controllers e:
- Remover quando o `GlobalExceptionHandler` consegue lidar (relacionado a TASK-EZ-CR-001)
- Manter quando necessario com log + comentario justificando

## Distribuicao

| Arquivo | Ocorrencias |
|---|---|
| `DiagnosticoLogsController` | 17 |
| `DiagnosticoInfraController` | 6 |
| `DiagnosticoController` | 4 |
| `AdminClientesController` | 4 |
| `AdminSeedController` | 4 |
| `WebhookPixController` | 3 |
| `AdminUsuariosTenantController` | 2 |
| `AdminAdminsController`, `AdminStatusController`, `AdminTenantsController`, `IaAnuncioController`, `AdminDiagnosticoController`, `WebhookGatewayController` | 1 cada |

## Padrao de fix

```csharp
// Caso 1 — remover (deixar GlobalExceptionHandler agir)
try { ... } catch (Exception ex) { return BadRequest(ex.Message); }
// →
// (sem try/catch — GlobalExceptionHandler mapeia)

// Caso 2 — manter com justificativa
try {
    // diagnostico nao deve quebrar a pagina inteira
    return Ok(await Diagnose(...));
} catch (Exception ex) {
    _logger.LogError(ex, "Diagnostico {Op} falhou — retornando vazio", nameof(Op));
    return Ok(new { erro = true, mensagem = "Diagnostico indisponivel" });
}
```

## Definicao de Pronto

- [ ] Cada caso revisado individualmente
- [ ] Casos triviais removidos (deixar GEH agir)
- [ ] Casos justificados mantidos com `LogError` + comentario "// motivo: ..."
- [ ] `grep catch \(Exception` em controllers retorna so casos justificados
- [ ] `dotnet build` verde + tests passam
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-17)
- Relacionado: TASK-EZ-CR-001 (mesmo eixo)
