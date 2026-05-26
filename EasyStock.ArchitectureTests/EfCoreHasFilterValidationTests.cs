using System.Text.RegularExpressions;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Anti-regressao do bug HasFilter snake_case vs PascalCase (commit 1ef3477e,
/// 2026-05-26): em PG, identificadores entre aspas duplas sao case-sensitive
/// — `"cep_inicio"` != `"CepInicio"`. O bug em FreteZonaConfiguration +
/// StorefrontConfiguration + VagaOcupadaConfiguration + WebhookProcessadoConfiguration
/// passou build local e testes architecture sem deteccao; so falhou em
/// release_command do fly em prod (SqlState 42703 column does not exist).
///
/// Este test valida que TODO filter de indice (HasFilter / migration filter)
/// referencia identificadores entre aspas que efetivamente existem como
/// propriedades ou ColumnNames da entidade.
/// </summary>
public class EfCoreHasFilterValidationTests
{
    [Fact]
    public void Todo_HasFilter_Referencia_Apenas_Colunas_Existentes_Na_Entidade()
    {
        // Instancia o DbContext apenas para acessar o model — sem conexao real
        // (UseNpgsql aceita connection string fake porque nada e' executado)
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql("Host=fake;Database=fake;Username=fake;Password=fake")
            .Options;
        using var context = new EasyStockDbContext(options);
        var model = context.Model;

        // Regex captura identificadores entre aspas duplas: "Nome", "snake_case",
        // ignorando funcoes (lower(), is null, etc) e literais entre aspas simples.
        var quotedIdentifierRegex = new Regex("\"([^\"]+)\"", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var entityType in model.GetEntityTypes())
        {
            // Coleta TODOS os nomes que podem aparecer legalmente num filter:
            // - Nome da propriedade CLR (PascalCase default)
            // - ColumnName explicito via HasColumnName (snake_case ou outro)
            var validColumns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in entityType.GetProperties())
            {
                validColumns.Add(prop.Name);
                var columnName = prop.GetColumnName();
                if (!string.IsNullOrEmpty(columnName))
                {
                    validColumns.Add(columnName);
                }
            }

            foreach (var index in entityType.GetIndexes())
            {
                var filter = index.GetFilter();
                if (string.IsNullOrWhiteSpace(filter))
                {
                    continue;
                }

                var matches = quotedIdentifierRegex.Matches(filter);
                foreach (Match m in matches)
                {
                    var ident = m.Groups[1].Value;
                    if (!validColumns.Contains(ident))
                    {
                        var entityName = entityType.ClrType?.Name ?? entityType.Name;
                        var indexName = index.GetDatabaseName() ?? string.Join("_", index.Properties.Select(p => p.Name));
                        var sampleColumns = string.Join(", ", validColumns.Take(8));
                        violations.Add(
                            $"{entityName} index '{indexName}': filter [{filter}] referencia identificador \"{ident}\" " +
                            $"que NAO corresponde a propriedade nem ColumnName da entidade. Validos: {sampleColumns}");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            because: "PG e case-sensitive em identificadores entre aspas duplas. Filtros de indice DEVEM usar " +
                     "exatamente o nome da coluna fisica (Property.Name ou HasColumnName). Bug historico " +
                     "(2026-05-26 PR #240): FreteZonaConfiguration.HasFilter(\"\\\"cep_inicio\\\" IS NOT NULL\") " +
                     "com coluna criada como \"CepInicio\" — aborta release_command no deploy prod. " +
                     "Violacoes encontradas:\n  - " + string.Join("\n  - ", violations));
    }
}
