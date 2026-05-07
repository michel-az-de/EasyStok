using System.Data.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EasyStock.Infra.Postgre.Services;

/// <summary>
/// Implementacao do <see cref="IFaturaNumeradorService"/> para PostgreSQL.
///
/// <para>
/// Usa <c>INSERT ... ON CONFLICT DO UPDATE RETURNING</c> em transacao curta —
/// atomico em multi-pod sem locking pessimista no agregado <c>Fatura</c>.
/// </para>
///
/// <para>
/// Em provedores nao-PG (SQLite em dev), usa fallback com <see cref="DbContext"/>
/// e bloqueio via SaveChanges (concorrencia limitada).
/// </para>
/// </summary>
public sealed class FaturaNumeradorService(EasyStockDbContext db) : IFaturaNumeradorService
{
    public async Task<string> GerarAsync(Guid empresaId, DateTime dataEmissao, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId obrigatorio.", nameof(empresaId));

        var ano = dataEmissao.Year;

        long ultimoNumero;

        if (db.Database.IsNpgsql())
        {
            ultimoNumero = await GerarPostgresAsync(empresaId, ano, ct);
        }
        else
        {
            ultimoNumero = await GerarFallbackAsync(empresaId, ano, ct);
        }

        return $"{ano}-{ultimoNumero:D6}";
    }

    private async Task<long> GerarPostgresAsync(Guid empresaId, int ano, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO fatura_contador ("EmpresaId", "Ano", "UltimoNumero", "AtualizadoEm")
            VALUES (@empresaId, @ano, 1, now())
            ON CONFLICT ("EmpresaId", "Ano") DO UPDATE SET
              "UltimoNumero" = fatura_contador."UltimoNumero" + 1,
              "AtualizadoEm" = now()
            RETURNING "UltimoNumero";
            """;

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        AddParam(cmd, "@empresaId", empresaId);
        AddParam(cmd, "@ano", ano);

        // Se ja existe transacao do EF, anexar.
        if (db.Database.CurrentTransaction is not null)
            cmd.Transaction = db.Database.CurrentTransaction.GetDbTransaction();

        var result = await cmd.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("Nao foi possivel gerar numero de fatura.");
        return Convert.ToInt64(result);
    }

    private async Task<long> GerarFallbackAsync(Guid empresaId, int ano, CancellationToken ct)
    {
        var contador = await db.FaturaContadores
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Ano == ano, ct);

        if (contador is null)
        {
            contador = new Domain.Entities.FaturaContador
            {
                EmpresaId = empresaId,
                Ano = ano,
                UltimoNumero = 1,
                AtualizadoEm = DateTime.UtcNow
            };
            db.FaturaContadores.Add(contador);
        }
        else
        {
            contador.UltimoNumero += 1;
            contador.AtualizadoEm = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return contador.UltimoNumero;
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
