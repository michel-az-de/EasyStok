using System.Data;
using System.Data.Common;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace EasyStock.Infra.Postgre.Security;

/// <summary>Escopo pré-auth/cross-tenant auditado, inclusive em conexão já aberta. Não altera o Begin legado.</summary>
public static class RowLevelSecurityScope
{
    public static async ValueTask<IAsyncDisposable> UseRowLevelSecurityBypassAsync(this EasyStockDbContext db, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        var flag = db.UseRowLevelSecurityBypass();
        string? previous = null;
        try
        {
            if (opened)
                await db.Database.OpenConnectionAsync(ct); // interceptor aplica a flag antes de qualquer leitura
            else
            {
                await using var query = Command(db, "SELECT current_setting('app.bypass_rls', true)");
                previous = await query.ExecuteScalarAsync(ct) as string ?? "false";
                await Set(db, "true", ct);
            }
            return new Scope(db, flag, opened, previous);
        }
        catch
        {
            try
            {
                if (opened) await db.Database.CloseConnectionAsync();
                else if (previous is not null)
                {
                    try { await Set(db, previous); }
                    catch { await connection.CloseAsync(); }
                }
            }
            finally { flag.Dispose(); }
            throw;
        }
    }

    private static DbCommand Command(EasyStockDbContext db, string sql)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }

    private static async Task Set(EasyStockDbContext db, string value, CancellationToken ct = default)
    {
        await using var command = Command(db, "SELECT set_config('app.bypass_rls', @value, false)");
        var parameter = command.CreateParameter();
        parameter.ParameterName = "value";
        parameter.Value = value;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed class Scope(EasyStockDbContext db, IDisposable flag, bool opened, string? previous) : IAsyncDisposable
    {
        private bool disposed;
        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                if (opened) await db.Database.CloseConnectionAsync();
                else if (db.Database.GetDbConnection().State == ConnectionState.Open)
                {
                    try { await Set(db, previous!); }
                    catch (PostgresException ex) when (ex.SqlState == "25P02" && db.Database.CurrentTransaction is not null)
                    {
                        // A transação já falhou: não mascarar o erro original. Seu rollback desfaz o SET do escopo.
                    }
                    catch
                    {
                        await db.Database.GetDbConnection().CloseAsync(); // fail closed: nunca reutilizar conexão com bypass incerto
                        throw;
                    }
                }
            }
            finally { flag.Dispose(); }
        }
    }
}
