# TASK-EZ-CR-011 — RequireGuid helper + remover 23 validacoes manuais

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-13)
**Prioridade:** P2
**Esforco:** P
**Status:** inbox

## Objetivo

Centralizar validacao de `Guid.Empty` num helper reusavel, eliminando 23 validacoes manuais duplicadas em 7 arquivos.

## Escopo

- [EasyStock.Api/Http/EasyStockControllerBase.cs](../../../EasyStock.Api/Http/EasyStockControllerBase.cs) (adicionar helper)
- 7 arquivos com `tenantId/empresaId == Guid.Empty`:
  - `AdminClientesController` (12 ocorrencias)
  - `EntityAuditController` (3)
  - `EasyStockControllerBase` (2)
  - `MobileManagementControllerBase` (2)
  - `IdempotencyMiddleware` (1)
  - `DevicePairingController` (1)
  - `OperationController` (2)

## Plano

```csharp
// EasyStockControllerBase.cs
protected IActionResult? RequireGuid(Guid value, string nomeRecurso)
{
    return value == Guid.Empty
        ? DataBadRequest($"{nomeRecurso} invalido.")
        : null;
}
```

```csharp
// ANTES (12x em AdminClientesController)
if (tenantId == Guid.Empty) return DataBadRequest("Cliente invalido.");

// DEPOIS
if (RequireGuid(tenantId, "Cliente") is { } error) return error;
```

**Alternativa mais elegante:** `[ValidGuid]` attribute para model binding (Mais trabalhoso).

## Definicao de Pronto

- [ ] Helper `RequireGuid` em `EasyStockControllerBase`
- [ ] 23 validacoes manuais substituidas
- [ ] `grep tenantId == Guid\.Empty|empresaId == Guid\.Empty` retorna so casos justificados
- [ ] `dotnet build` verde + tests passam
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-13)
