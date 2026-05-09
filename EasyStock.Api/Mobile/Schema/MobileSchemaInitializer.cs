using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Schema;

/// <summary>
/// Aplica o schema do módulo Mobile via SQL raw no startup da API.
///
/// Carrega TODOS os arquivos <c>NNN_*.sql</c> da pasta <c>Mobile/Schema/</c>
/// em ordem alfabética. Cada arquivo é idempotente (<c>CREATE TABLE IF NOT
/// EXISTS</c>, <c>ALTER TABLE ... ADD COLUMN IF NOT EXISTS</c>, etc), então
/// rodar múltiplas vezes é seguro. Não entra no <c>__EFMigrationsHistory</c>;
/// é uma decisão consciente para manter o módulo isolado do pipeline EF.
///
/// Convenção de ordem: <c>001_*.sql</c> roda antes de <c>002_*.sql</c>.
/// Cada SQL é seu próprio batch; falha de um interrompe os seguintes.
/// </summary>
public static class MobileSchemaInitializer
{
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

        // ExecuteSqlRawAsync faz string.Format internamente — quebra com FormatException
        // se o SQL contiver chaves literais (jsonb defaults, blocos DO $$, etc). Usamos
        // DbCommand direto pra passar o SQL exatamente como esta no arquivo.
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);

        try
        {
            foreach (var path in files)
            {
                var sql = await File.ReadAllTextAsync(path, ct);
                try
                {
                    await using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 120;
                    await cmd.ExecuteNonQueryAsync(ct);
                    logger.LogInformation("Mobile schema aplicado: {File}", Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Falha ao aplicar Mobile schema {File}", Path.GetFileName(path));
                    throw;
                }
            }
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }
    }
}
