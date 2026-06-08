using EasyStock.Application.Ports.Output;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Data.Interceptors;

/// <summary>
/// Invalida o cache de saldo do produto (produto-detalhe) sempre que um
/// <see cref="ItemEstoque"/> e adicionado/modificado/removido e o <c>SaveChanges</c>
/// sucede. Captura mutacoes de QUALQUER call site — use cases, servicos (pedido),
/// reconciliador mobile via <c>_db</c> direto — sem que cada um precise lembrar de
/// invalidar (chokepoint). Espelha <c>AssinaturaCacheInvalidationInterceptor</c>.
///
/// <para>
/// Coleta <c>(EmpresaId, ProdutoId)</c> em <c>SavingChanges</c> (Added/Modified/Deleted)
/// e dispara a invalidacao em <c>SavedChanges</c> (apos persistencia bem-sucedida).
/// Se o SaveChanges falhar, nada e invalidado (<c>Discard</c>) — o proximo update
/// recacheia quando o TTL expirar. Filtro AMPLO (qualquer mudanca em ItemEstoque,
/// nao so QuantidadeAtual): over-invalidar e seguro (cache-clear -> recomputo do DB)
/// e cobre o caso analytics-por-Status da Fatia 2.
/// </para>
///
/// <para>
/// Fronteira: pega toda mutacao via SaveChanges/ChangeTracker. Mutacoes bulk EF7
/// (<c>ExecuteUpdate</c>/<c>ExecuteDelete</c>) ou raw SQL/Dapper passam por FORA e
/// teriam que invalidar manualmente — medido que nenhum caminho de producao muta
/// <c>QuantidadeAtual</c> por bulk/raw hoje.
/// </para>
///
/// <para>
/// Stateless por instancia (Singleton no DI): usa <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
/// por <see cref="DbContext"/> para carregar o set detectado de Saving ate Saved,
/// atendendo DbContext concorrentes sem campo de instancia. Plugado por ULTIMO via
/// <c>DbContextOptionsBuilder.AddInterceptors(...)</c>.
/// </para>
/// </summary>
public sealed class EstoqueSaldoCacheInvalidationInterceptor(
    IProdutoCacheInvalidator invalidator,
    ILogger<EstoqueSaldoCacheInvalidationInterceptor> logger) : SaveChangesInterceptor
{
    // Slot por DbContext que carrega os pares (empresa, produto) detectados em
    // Saving ate a notificacao de Saved. Evitamos campo de instancia: este
    // interceptor e Singleton e atende DbContext concorrentes.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, HashSet<(Guid Empresa, Guid Produto)>> Pending = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // EF Core NAO cruza sync/async: SaveChanges() -> SavedChanges; SaveChangesAsync()
    // -> SavedChangesAsync. Por isso o flush e fatorado e chamado dos DOIS. O caminho
    // de producao dos mutadores e async; o sync e defensivo (nenhum SaveChanges()
    // sincrono muta estoque em producao hoje). GetAwaiter().GetResult() e seguro aqui:
    // backend in-memory completa sincrono e ASP.NET Core nao tem SynchronizationContext
    // (sem risco de deadlock). Se Redis (cache de rede) for habilitado, revisitar.
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        FlushAsync(eventData.Context).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await FlushAsync(eventData.Context);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Discard(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries<ItemEstoque>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            // ATUAL via entry.Entity (CurrentValue). EmpresaId/ProdutoId sao escalares
            // na entidade — nao dependem da navegacao Produto (nula num Added).
            TryAdd(context, entry.Entity.EmpresaId, entry.Entity.ProdutoId);

            // ORIGINAL mora no state manager (entry.OriginalValues), NAO no entry.Entity.
            // Added nao tem original. Reparent de ProdutoId/EmpresaId -> invalida o antigo tambem.
            if (entry.State != EntityState.Added)
            {
                var empresaOrig = (Guid?)entry.OriginalValues[nameof(ItemEstoque.EmpresaId)] ?? Guid.Empty;
                var produtoOrig = (Guid?)entry.OriginalValues[nameof(ItemEstoque.ProdutoId)] ?? Guid.Empty;
                if (produtoOrig != entry.Entity.ProdutoId || empresaOrig != entry.Entity.EmpresaId)
                    TryAdd(context, empresaOrig, produtoOrig);
            }
        }
    }

    private void TryAdd(DbContext context, Guid empresa, Guid produto)
    {
        if (empresa == Guid.Empty || produto == Guid.Empty)
        {
            // Nao deveria ocorrer: o app seta EmpresaId/ProdutoId em todas as vias antes
            // do save (nenhum interceptor os carimba). LogWarning em vez de skip silencioso
            // — e um bug de staleness silencioso, tem que ficar alto se o impossivel ocorrer.
            logger.LogWarning(
                "EstoqueSaldoCacheInvalidationInterceptor: ItemEstoque com EmpresaId/ProdutoId vazio (empresa={Empresa}, produto={Produto}) — invalidacao pulada.",
                empresa, produto);
            return;
        }
        GetOrCreateBucket(context).Add((empresa, produto));
    }

    private async Task FlushAsync(DbContext? context)
    {
        if (context is null) return;
        if (!Pending.TryGetValue(context, out var bucket)) return;
        Pending.Remove(context);

        try
        {
            foreach (var grupo in bucket.GroupBy(p => p.Empresa))
                await invalidator.InvalidarSaldoAsync(grupo.Key, grupo.Select(p => p.Produto));
        }
        catch (Exception ex)
        {
            // Defesa extra: o invalidator ja e best-effort por produto, mas qualquer
            // falha aqui nao pode quebrar o SavedChanges pos-persist (senao 500 ->
            // re-submit -> saldo dobrado).
            logger.LogError(ex, "EstoqueSaldoCacheInvalidationInterceptor: falha no flush de invalidacao de saldo.");
        }
    }

    private static void Discard(DbContext? context)
    {
        if (context is null) return;
        Pending.Remove(context);
    }

    private static HashSet<(Guid Empresa, Guid Produto)> GetOrCreateBucket(DbContext context)
    {
        if (Pending.TryGetValue(context, out var existing)) return existing;
        var fresh = new HashSet<(Guid Empresa, Guid Produto)>();
        Pending.Add(context, fresh);
        return fresh;
    }
}
