using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Anti-regressao do incidente #465 (2026-06-04): 3 migrations foram commitadas
/// SEM o arquivo <c>.Designer.cs</c> e, portanto, SEM o atributo
/// <c>[Migration]</c> (que o EF gera no Designer). O scanner do EF Core
/// (<c>MigrationsAssembly</c>) so reconhece subclasses de <c>Migration</c> que
/// tenham <c>[MigrationAttribute]</c>; as 3 ficaram invisiveis: nunca apareciam
/// em <c>dotnet ef migrations list</c>, nunca eram aplicadas por
/// <c>database update</c> / <c>RunMigrationsOnStartup</c>, e o snapshot ja
/// refletia as colunas (<c>has-pending-model-changes</c> = false). Resultado:
/// banco novo (CI/Testcontainers, deploy novo, DR) subia SEM as colunas ->
/// <c>42703 column does not exist</c> silencioso em runtime, que parecia bug de
/// feature.
///
/// Este teste varre o assembly de migrations por reflexao (NAO toca filesystem,
/// robusto sob CI/pathmap) e falha se qualquer subclasse concreta de
/// <c>Migration</c> nao tiver <c>[MigrationAttribute]</c> — ou seja, ficou
/// invisivel ao EF. Pega a reincidencia no gate, antes do deploy.
/// </summary>
[Trait("Category", "Architecture")]
public class MigrationDesignerHygieneTests
{
    [Fact]
    public void Toda_Migration_Tem_Atributo_Migration_E_Portanto_E_Visivel_Ao_EF()
    {
        var migrationsAssembly = typeof(EasyStockDbContext).Assembly;

        var migrationTypes = migrationsAssembly.GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        // Guarda contra falso-verde: se o filtro nao achar migration nenhuma,
        // o assembly errado foi carregado (ou um refactor quebrou a deteccao),
        // o que mascararia a regressao silenciosamente.
        migrationTypes.Should().NotBeEmpty(
            because: "o assembly EasyStock.Infra.Postgre deve conter migrations concretas; " +
                     "zero indica assembly/filtro errado mascarando a regressao do #465.");

        var invisiveisAoEf = migrationTypes
            .Where(t => t.GetCustomAttributes(typeof(MigrationAttribute), inherit: false).Length == 0)
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        invisiveisAoEf.Should().BeEmpty(
            because: "migration sem [Migration] fica invisivel ao EF runtime (incidente #465): nao aparece " +
                     "em 'dotnet ef migrations list', nunca e aplicada, e causa 42703 em banco novo. Causa raiz " +
                     "historica: o arquivo .Designer.cs (que carrega o atributo) nao foi versionado. Correcao: " +
                     "regenerar via 'dotnet ef migrations add' OU consolidar o DDL numa migration valida e " +
                     "remover a orfa. Migrations invisiveis encontradas:\n  - " + string.Join("\n  - ", invisiveisAoEf));
    }
}
