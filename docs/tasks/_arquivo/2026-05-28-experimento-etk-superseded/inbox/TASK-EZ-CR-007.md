# TASK-EZ-CR-007 — Refatorar AdminClientesController.GetAtividade + usar PiiMaskingHelper

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-9)
**Prioridade:** P2
**Esforco:** M
**Status:** inbox

## Objetivo

Extrair logica do `AdminClientesController.GetAtividade` (115 linhas, mescla em memoria) para UseCase com query SQL UNION ALL, e substituir helpers privados de PII pelo `PiiMaskingHelper` ja existente.

## Problemas

1. `GetAtividade` (linhas ~186-300) mescla `AuditLog` (tenant) + `AdminAuditLog` (admin) em memoria com `Skip((page-1)*pageSize).Take(pageSize)` — autor anotou "P1 OK, P2 vira UNION ALL"
2. Helpers privados `MascararDetalhes`, `MascararIp`, `MascararEmail` no controller — ja existe `EasyStock.Api/Utilities/PiiMaskingHelper.cs`
3. 12 ocorrencias `tenantId == Guid.Empty` no proprio controller (combina com TASK-EZ-CR-011)
4. `ExportarDados` (~423-496, 74 linhas) — montagem ZIP no controller

## Escopo

- [EasyStock.Api/Controllers/AdminClientesController.cs](../../../EasyStock.Api/Controllers/AdminClientesController.cs)
- [EasyStock.Api/Utilities/PiiMaskingHelper.cs](../../../EasyStock.Api/Utilities/PiiMaskingHelper.cs) (validar API existente)
- Novo `EasyStock.Application/UseCases/Admin/ListarAtividadeTenantUseCase.cs`
- Novo repository method para UNION ALL

## Plano

### Fase 1 — UseCase + UNION ALL
- Criar `ListarAtividadeTenantUseCase(tenantId, page, pageSize, filtros)` retornando `PagedResult<AtividadeItemDto>`
- Repository expoe `IAuditLogRepository.ListarAtividadeUnificadaAsync(...)` com SQL nativa UNION ALL ordenada por `dataHora DESC` com paginacao SQL

### Fase 2 — PiiMaskingHelper
- Mover/usar metodos `Mascarar*` de `PiiMaskingHelper`
- Validar API existente e estender se necessario
- Remover metodos privados de `AdminClientesController`

### Fase 3 — Extracao de ExportarDados (opcional, se ainda houver folego)
- `ExportarDadosTenantUseCase` para montar ZIP em layer apropriada

## Definicao de Pronto

- [ ] `GetAtividade` no controller tem <30 linhas (so chama use case + retorna)
- [ ] Use case + repository com SQL UNION ALL implementados
- [ ] Helpers privados de PII removidos; `PiiMaskingHelper` usado
- [ ] Unit tests do use case (mock repository) com 8+ cenarios
- [ ] Integration test valida que UNION ALL retorna mesma ordem/conteudo da impl anterior
- [ ] Benchmark antes/depois (deve ser >= 2x mais rapido para pagina 5+)
- [ ] `dotnet build` verde

## Riscos

- SQL UNION ALL exige cuidado com tipos de coluna (cast explicito)
- Mudanca de comportamento se filtros funcionavam de modo sutil em memoria

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-9)
