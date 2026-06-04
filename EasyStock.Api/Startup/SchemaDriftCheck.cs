using System.Data;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EasyStock.Api.Startup;

/// <summary>
/// Detecta "schema drift": colunas que o modelo EF espera mas que NÃO existem no banco,
/// mesmo com a migration registrada como aplicada no <c>__EFMigrationsHistory</c>.
///
/// Isso acontece quando um volume Postgres é reaproveitado e o histórico de migrations
/// é reconciliado sem o DDL ter rodado de fato (ex.: schema mobile/raw que "reivindica"
/// tabelas). O sintoma é um <c>42703 column X does not exist</c> recorrente em runtime,
/// que parece "bug de feature" e custou várias rodadas de investigação às cegas.
///
/// Read-only: só lê <c>information_schema</c> e o modelo EF. Não altera nada.
/// </summary>
public static class SchemaDriftCheck
{
    /// <summary>
    /// Colunas de sistema do PostgreSQL. O modelo EF as mapeia (ex.: <c>xmin</c> como
    /// concurrency token via <c>HasColumnName("xmin")</c>), mas elas NÃO aparecem em
    /// <c>information_schema.columns</c> — existem implicitamente em toda tabela. Ignorá-las
    /// evita falso-positivo de "drift" (do contrário todo boot acusaria 1 xmin por entidade).
    /// </summary>
    private static readonly HashSet<string> PostgresSystemColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "xmin", "xmax", "cmin", "cmax", "ctid", "tableoid", "oid"
    };

    /// <summary>
    /// Retorna a lista de <c>tabela.coluna</c> (schema public) que o modelo espera mas
    /// que faltam no banco. Vazia = sem drift.
    /// </summary>
    public static async Task<List<string>> FindMissingColumnsAsync(EasyStockDbContext db)
    {
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var conn = db.Database.GetDbConnection();
        var opened = false;
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync();
            opened = true;
        }
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public'";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                actual.Add(reader.GetString(0));
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }

        var missing = new List<string>();
        foreach (var entity in db.Model.GetEntityTypes())
        {
            if (entity.IsOwned()) continue;

            var tableName = entity.GetTableName();
            if (string.IsNullOrEmpty(tableName)) continue; // keyless/view-mapped

            var schema = entity.GetSchema();
            if (!string.IsNullOrEmpty(schema) && !string.Equals(schema, "public", StringComparison.OrdinalIgnoreCase))
                continue; // só checa o schema public

            var storeObject = StoreObjectIdentifier.Table(tableName, schema);
            foreach (var prop in entity.GetProperties())
            {
                var column = prop.GetColumnName(storeObject);
                if (string.IsNullOrEmpty(column)) continue; // table splitting: não mapeia nesta tabela
                if (PostgresSystemColumns.Contains(column)) continue; // xmin & cia: invisíveis ao information_schema

                var key = $"{tableName}.{column}";
                if (!actual.Contains(key))
                    missing.Add(key);
            }
        }

        return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
