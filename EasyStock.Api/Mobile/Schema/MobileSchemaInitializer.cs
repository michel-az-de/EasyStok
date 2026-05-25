using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EasyStock.Api.Mobile.Schema;

/// <summary>
/// Aplica o schema do módulo Mobile via SQL raw no startup da API.
///
/// Carrega TODOS os arquivos <c>NNN_*.sql</c> da pasta <c>Mobile/Schema/</c>
/// em ordem alfabética. Cada arquivo é idempotente; falhas conhecidas
/// (42P07 table existe, 42701 column existe, 42703 column nao existe ao
/// criar index — sintoma de tabela parcial) são logadas como warning e o
/// initializer SEGUE adiante. Apenas erros realmente fatais sobem como
/// exception (que tambem e' capturada pelo try/catch do Program.cs e
/// transformada em log error sem matar o app).
///
/// Convenção: <c>001_*.sql</c> roda antes de <c>002_*.sql</c>.
/// </summary>
public static class MobileSchemaInitializer
{
    private static readonly HashSet<string> SqlStatesToleraveis = new(StringComparer.OrdinalIgnoreCase)
    {
        "42P07", // duplicate_table
        "42701", // duplicate_column
        "42703", // undefined_column (ex.: CREATE INDEX em coluna que ficou de fora por estado parcial)
        "42P06", // duplicate_schema
        "42P16", // invalid_table_definition (ex.: ALTER TABLE em conflito conhecido)
        "0A000"  // feature_not_supported (ex.: certas operacoes em pg14 vs pg15)
    };

    public static async Task InitializeAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct = default)
    {
        var schemaDir = Path.Combine(AppContext.BaseDirectory, "Mobile", "Schema");
        if (!Directory.Exists(schemaDir))
        {
            logger.LogWarning("Mobile schema dir não encontrado em {Path}; endpoints /api/mobile/* podem falhar.", schemaDir);
            return;
        }

        var files = Directory.GetFiles(schemaDir, "*.sql").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            logger.LogWarning("Nenhum arquivo .sql em {Path}.", schemaDir);
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);

        try
        {
            foreach (var path in files)
            {
                var fileName = Path.GetFileName(path);
                var sql = await File.ReadAllTextAsync(path, ct);
                var statements = SplitSqlStatements(sql);

                var aplicados = 0;
                var skipados = 0;
                var falhouFatal = false;

                foreach (var stmt in statements)
                {
                    try
                    {
                        await using var cmd = connection.CreateCommand();
                        cmd.CommandText = stmt;
                        cmd.CommandTimeout = 120;
                        await cmd.ExecuteNonQueryAsync(ct);
                        aplicados++;
                    }
                    catch (PostgresException pgEx) when (SqlStatesToleraveis.Contains(pgEx.SqlState ?? ""))
                    {
                        skipados++;
                        logger.LogWarning(
                            "Mobile schema {File}: skip statement (SqlState={SqlState}, msg={Msg}, sql={Sql})",
                            fileName, pgEx.SqlState, pgEx.MessageText,
                            stmt.Length > 120 ? stmt[..120] + "..." : stmt);

                        // Postgres aborta a transacao apos um erro; precisa rollback antes de continuar.
                        await ResetTransactionStateAsync(connection, ct);
                    }
                    catch (Exception ex)
                    {
                        falhouFatal = true;
                        logger.LogError(ex,
                            "Mobile schema {File}: ERRO FATAL no statement (sql={Sql})",
                            fileName, stmt.Length > 120 ? stmt[..120] + "..." : stmt);
                        await ResetTransactionStateAsync(connection, ct);
                        break;
                    }
                }

                if (falhouFatal)
                    logger.LogError(
                        "Mobile schema {File}: parou cedo apos erro fatal. Aplicados={Aplicados}, Skipados={Skipados}.",
                        fileName, aplicados, skipados);
                else
                    logger.LogInformation(
                        "Mobile schema aplicado: {File} (aplicados={Aplicados}, skipados={Skipados}).",
                        fileName, aplicados, skipados);
            }
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Split simples por ';' em final de linha. Ignora BEGIN/COMMIT/ROLLBACK
    /// (rodamos cada statement em sua propria mini-transacao implicita do
    /// Npgsql). Nao trata ';' dentro de strings literais ou dollar-quoted —
    /// caso do .sql do Mobile que so tem DDL simples.
    /// </summary>
    private static IReadOnlyList<string> SplitSqlStatements(string sql)
    {
        var pieces = sql.Split(';');
        var result = new List<string>(pieces.Length);
        foreach (var raw in pieces)
        {
            var trimmed = raw.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            // Remove linhas de comment puro do statement.
            var lines = trimmed.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("--"));
            var cleaned = string.Join('\n', lines).Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
                continue;
            // Pula BEGIN/COMMIT/ROLLBACK explicitos do arquivo — cada statement vira sua propria query.
            if (cleaned.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)
             || cleaned.Equals("COMMIT", StringComparison.OrdinalIgnoreCase)
             || cleaned.Equals("ROLLBACK", StringComparison.OrdinalIgnoreCase))
                continue;
            result.Add(cleaned);
        }
        return result;
    }

    private static async Task ResetTransactionStateAsync(System.Data.Common.DbConnection connection, CancellationToken ct)
    {
        try
        {
            await using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK";
            await rollback.ExecuteNonQueryAsync(ct);
        }
        catch
        {
            // Se nao havia transacao aberta, ROLLBACK retorna warning — ignora.
        }
    }
}
