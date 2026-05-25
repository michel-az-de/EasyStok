using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EasyStock.Infra.Postgre.Data.Interceptors;

/// <summary>
/// Preenche <c>CriadoEm</c> / <c>AlteradoEm</c> automaticamente em todas as
/// entidades que tenham essas propriedades, e converte <c>Remove(...)</c> em
/// soft-delete (set <c>IsDeletado=true</c>) quando a entidade tem essa flag.
/// <para>
/// Discovery por convencao reflexiva — entidade nao precisa implementar
/// interface marker. Comportamento ativado a cada SaveChanges sincrono ou
/// async via DbContextOptionsBuilder.AddInterceptors(...).
/// </para>
/// <para>
/// Auditoria 2026-05-06: anteriormente CriadoEm/AlteradoEm eram preenchidos
/// manualmente em factory methods das entidades. Esquecimento => coluna NULL
/// ou default em prod, quebrando relatorios e auditorias.
/// </para>
/// </summary>
public sealed class AuditTimestampsInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditing(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditing(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ApplyAuditing(DbContext? context)
    {
        if (context is null) return;
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    SetIfDefault(entry, "CriadoEm", now);
                    SetIfDefault(entry, "AlteradoEm", now);
                    break;

                case EntityState.Modified:
                    // Nunca alterar CriadoEm em update.
                    PreserveOriginal(entry, "CriadoEm");
                    SetAlways(entry, "AlteradoEm", now);
                    break;

                case EntityState.Deleted:
                    if (HasProperty(entry, "IsDeletado", typeof(bool)))
                    {
                        // Soft-delete: vira Modified com IsDeletado=true.
                        entry.State = EntityState.Modified;
                        SetAlways(entry, "IsDeletado", true);
                        SetAlways(entry, "AlteradoEm", now);
                        PreserveOriginal(entry, "CriadoEm");
                    }
                    break;
            }
        }
    }

    private static bool HasProperty(EntityEntry entry, string name, Type expectedType)
    {
        var prop = entry.Metadata.FindProperty(name);
        if (prop is null) return false;
        var t = prop.ClrType;
        return t == expectedType || Nullable.GetUnderlyingType(t) == expectedType;
    }

    private static void SetIfDefault(EntityEntry entry, string name, object value)
    {
        var prop = entry.Metadata.FindProperty(name);
        if (prop is null) return;

        var current = entry.Property(name).CurrentValue;
        var t = prop.ClrType;
        var underlying = Nullable.GetUnderlyingType(t) ?? t;

        bool isDefault = current is null
                      || (underlying == typeof(DateTime) && current is DateTime dt && dt == default)
                      || (underlying == typeof(DateTimeOffset) && current is DateTimeOffset dto && dto == default);

        if (isDefault)
            entry.Property(name).CurrentValue = value;
    }

    private static void SetAlways(EntityEntry entry, string name, object value)
    {
        var prop = entry.Metadata.FindProperty(name);
        if (prop is null) return;
        entry.Property(name).CurrentValue = value;
        entry.Property(name).IsModified = true;
    }

    private static void PreserveOriginal(EntityEntry entry, string name)
    {
        var prop = entry.Metadata.FindProperty(name);
        if (prop is null) return;
        // Marca CriadoEm como nao-modificado pra que UPDATE nao reescreva,
        // mesmo se o consumidor tiver setado um novo valor por engano.
        entry.Property(name).IsModified = false;
    }
}
