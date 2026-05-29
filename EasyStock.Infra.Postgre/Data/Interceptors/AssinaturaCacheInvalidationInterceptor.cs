using EasyStock.Application.Ports.Output.Caching;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasyStock.Infra.Postgre.Data.Interceptors;

/// <summary>
/// Invalida o <see cref="ISubscriptionStatusCache"/> sempre que uma
/// <see cref="AssinaturaEmpresa"/> e adicionada/modificada/deletada e o
/// <c>SaveChanges</c> sucede. Captura mutacoes de qualquer call site —
/// use cases, controllers, jobs — sem que cada um precise lembrar de
/// chamar <c>Invalidate</c>.
///
/// <para>
/// Coleta os <c>EmpresaId</c> em <c>SavingChanges</c> (estado <c>Added</c>,
/// <c>Modified</c> ou <c>Deleted</c>) e dispara <c>Invalidate</c> em
/// <c>SavedChanges</c> (apos persistencia bem-sucedida). Se o
/// SaveChanges falhar, nada e invalidado — proximo update vai recachear
/// quando o TTL expirar.
/// </para>
///
/// <para>
/// Stateless por instancia — usa <see cref="DbContextEventData.Context"/>
/// para descobrir o ChangeTracker. Singleton no DI, plugado via
/// <c>DbContextOptionsBuilder.AddInterceptors(...)</c>.
/// </para>
/// </summary>
public sealed class AssinaturaCacheInvalidationInterceptor(ISubscriptionStatusCache cache)
    : SaveChangesInterceptor
{
    // Slot por DbContext que carrega os empresaIds detectados em Saving
    // ate a notificacao de Saved. Evitamos campo de instancia aqui pois
    // este interceptor e Singleton e atende DbContext concorrentes.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, HashSet<Guid>> Pending = new();

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

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Flush(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Flush(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
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

    private static void Capture(DbContext? context)
    {
        if (context is null) return;

        HashSet<Guid>? bucket = null;
        foreach (var entry in context.ChangeTracker.Entries<AssinaturaEmpresa>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            // Captura tanto o valor atual (default) quanto o original
            // — UPDATE pode ter trocado EmpresaId (improvavel, mas seguro).
            var current = entry.Entity.EmpresaId;
            var original = entry.State == EntityState.Added
                ? Guid.Empty
                : (Guid?)entry.OriginalValues[nameof(AssinaturaEmpresa.EmpresaId)] ?? Guid.Empty;

            bucket ??= GetOrCreateBucket(context);
            if (current != Guid.Empty) bucket.Add(current);
            if (original != Guid.Empty && original != current) bucket.Add(original);
        }
    }

    private void Flush(DbContext? context)
    {
        if (context is null) return;
        if (!Pending.TryGetValue(context, out var bucket)) return;

        foreach (var empresaId in bucket)
            cache.Invalidate(empresaId);

        Pending.Remove(context);
    }

    private static void Discard(DbContext? context)
    {
        if (context is null) return;
        Pending.Remove(context);
    }

    private static HashSet<Guid> GetOrCreateBucket(DbContext context)
    {
        if (Pending.TryGetValue(context, out var existing)) return existing;
        var fresh = new HashSet<Guid>();
        Pending.Add(context, fresh);
        return fresh;
    }
}
