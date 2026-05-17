using EasyStock.Application.Ports.Output.Security;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Security;

/// <summary>
/// Implementa <see cref="IRowLevelSecurityBypass"/> delegando ao
/// <see cref="EasyStockDbContext.UseRowLevelSecurityBypass"/>, que controla
/// tanto o Global Query Filter do EF quanto a flag de bypass do interceptor
/// que faz <c>SET app.bypass_rls = 'on'</c> no Postgres.
/// </summary>
public sealed class RowLevelSecurityBypass(EasyStockDbContext db) : IRowLevelSecurityBypass
{
    public IDisposable Begin() => db.UseRowLevelSecurityBypass();
}
