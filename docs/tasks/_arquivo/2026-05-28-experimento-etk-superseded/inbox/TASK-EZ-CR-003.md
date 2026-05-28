# TASK-EZ-CR-003 — Atomicidade em SyncMutationDispatcher + SyncAutoLinker (race conditions mobile)

**Origem:** Auditoria E2E EasyStock.Api 2026-05-27 (ACHADO-3 + ACHADO-4)
**Prioridade:** P1
**Esforco:** M
**Status:** inbox

## Objetivo

Eliminar race conditions e inconsistencias em operacoes mobile de sync, garantindo atomicidade transacional e prevenindo duplicacao via unique constraints.

## Problemas concretos

1. **`SyncMutationDispatcher.ApplyOrder`** (linhas aprox. 270-300, 420-442) — side-effects (`CreateVendaForDeliveredOrderAsync`, `ApplyDeltaAsync`) executados antes de `SaveChangesAsync()`. Excecao deixa estado inconsistente (Venda sem Pedido fechado).
2. **`SyncAutoLinker.EnsurePagamentoEntregueAsync`** — cria `PedidoPagamento` + `MovimentoCaixa` sem constraint unica `(pedidoId, metodo)` → duplicacao em sync concorrente.
3. **`SyncAutoLinker.TryAutoLinkBatchesAsync`** — N+1 (1 query/item de lote).

## Escopo

- [EasyStock.Api/Mobile/Services/SyncMutationDispatcher.cs](../../../EasyStock.Api/Mobile/Services/SyncMutationDispatcher.cs)
- [EasyStock.Api/Mobile/Services/SyncAutoLinker.cs](../../../EasyStock.Api/Mobile/Services/SyncAutoLinker.cs)
- Migration nova para unique constraint
- Tests de integration (`EasyStock.Infra.Postgre.IntegrationTests`) com cenarios concorrentes

**Pre-requisito:** confirmar linhas exatas via leitura direta antes de comecar (achado veio do agente Explore, nao validado linha-a-linha).

## Padrao de fix

### Fix 1 — Atomicidade em ApplyOrder

```csharp
public async Task ApplyOrderAsync(MobileOrderDto mobileO, CancellationToken ct)
{
    await using var tx = await _db.Database.BeginTransactionAsync(ct);
    try {
        var pedido = await CarregarPedido(mobileO.Id, ct);
        AplicarMutacoes(pedido, mobileO);
        if (pedido.Status == StatusPedido.Entregue)
            await _saleSync.CreateVendaForDeliveredOrderAsync(pedido, ct);
        await _stockReconciler.ApplyDeltaAsync(pedido, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    } catch {
        await tx.RollbackAsync(ct);
        throw;
    }
}
```

### Fix 2 — Unique constraint

```csharp
// Migration
migrationBuilder.CreateIndex(
    name: "IX_PedidoPagamentos_PedidoId_Metodo",
    table: "PedidoPagamentos",
    columns: new[] { "PedidoId", "Metodo" },
    unique: true);
```

### Fix 3 — Batch product loading

```csharp
// ANTES (N+1)
foreach (var item in lote.Itens) {
    var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == item.ProductId, ct);
    ...
}

// DEPOIS
var ids = lote.Itens.Select(i => i.ProductId).Distinct().ToList();
var products = await _db.Set<Product>()
    .Where(p => ids.Contains(p.Id))
    .ToDictionaryAsync(p => p.Id, ct);
foreach (var item in lote.Itens) {
    if (!products.TryGetValue(item.ProductId, out var product)) continue;
    ...
}
```

## Definicao de Pronto

- [ ] `BeginTransactionAsync` envolve TODOS os side-effects em `ApplyOrder`
- [ ] Unique constraint `IX_PedidoPagamentos_PedidoId_Metodo` adicionada via migration
- [ ] N+1 eliminado em `TryAutoLinkBatchesAsync`
- [ ] Integration test: 2 requests concorrentes para mesmo pedido → 1 sucesso + 1 conflict 409 (nao 2 sucessos)
- [ ] Integration test: excecao no meio de `ApplyOrder` → estado revertido
- [ ] `dotnet build` verde + tests verdes
- [ ] PR mergeado

## Riscos

- Migration em prod requer `RunMigrationsOnStartup` ou rollout coordenado (R9)
- Unique constraint pode falhar em dados existentes — validar antes de migration
- Transacao adicional pode aumentar latencia mobile sync — medir antes/depois

## Referencias

- Relatorio: `docs/dev/code-reviews/2026-05-27-easystok-api-e2e.md` (ACHADO-3 + ACHADO-4)
