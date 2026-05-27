# TASK-EZ-CR-020 — Boundary tests para SubscriptionGateMiddleware.TrialExpiradoSemPlanoAtivo

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-29)
**Prioridade:** P3
**Esforco:** P
**Status:** inbox

## Objetivo

Adicionar testes de boundary para a logica de trial expirado no `SubscriptionGateMiddleware`, cobrindo casos limite (`DataFim == now`, `DataFim == null`, etc).

## Escopo

- [EasyStock.Api/Middleware/SubscriptionGateMiddleware.cs](../../../EasyStock.Api/Middleware/SubscriptionGateMiddleware.cs) (linhas ~104-120)
- Novo: `EasyStock.Api.UnitTests/Middleware/SubscriptionGateMiddlewareTests.cs`

## Cenarios

Validar `TrialExpiradoSemPlanoAtivo()` (ou metodo equivalente) com:

1. `DataFim == null` (nunca pagou) → bloqueia
2. `DataFim == now` (exatamente expirado) → bloqueia ou nao? (validar invariant)
3. `DataFim == now - 1s` (acabou de expirar) → bloqueia
4. `DataFim == now + 1s` (ainda valido) → libera
5. `DataFim == DateTime.MinValue` → bloqueia
6. `DataFim == DateTime.MaxValue` → libera
7. Sem `Assinatura` no DB → bloqueia
8. Plano ativo + trial expirado → libera
9. Plano cancelado + trial expirado → bloqueia

## Definicao de Pronto

- [ ] 9 cenarios de boundary cobertos em unit tests
- [ ] Invariants documentadas como comentario na classe
- [ ] `dotnet test --filter "FullyQualifiedName~SubscriptionGate"` passa
- [ ] `dotnet build` verde
- [ ] PR mergeado

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-29)
