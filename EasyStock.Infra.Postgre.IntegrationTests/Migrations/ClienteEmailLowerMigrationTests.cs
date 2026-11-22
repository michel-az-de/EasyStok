using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

/// <summary>#3101: a migração cria a coluna gerada <c>clientes.email_lower</c> e o índice da busca por e-mail; o Down desfaz.</summary>
[Collection("PostgreSqlTestCollection")]
public class ClienteEmailLowerMigrationTests(PostgreSqlDatabaseFixture fixture)
{
    private const string Migracao = "AddClienteEmailLower";

    [SkippableFact]
    public async Task Down_remove_a_coluna_e_o_indice_e_Up_traz_de_volta()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await fixture.ResetDatabaseAsync();

        await using var db = fixture.CreateDbContext();
        var migracoes = db.Database.GetMigrations().ToList();
        var alvo = migracoes.Single(m => m.EndsWith("_" + Migracao, StringComparison.Ordinal));
        var migrator = db.GetService<IMigrator>();

        (await Existe(db)).Should().Be((true, true), "o banco recém-migrado já tem coluna e índice");

        await migrator.MigrateAsync(migracoes[migracoes.IndexOf(alvo) - 1]);
        (await Existe(db)).Should().Be((false, false), "o Down remove os dois");

        await migrator.MigrateAsync(alvo);
        (await Existe(db)).Should().Be((true, true), "o Up recria os dois");
        // O banco é da coleção inteira: volta ao topo, não só até esta migração (#2096, #3163).
        await migrator.MigrateAsync();
    }

    private static async Task<(bool Coluna, bool Indice)> Existe(DbContext db)
    {
        var coluna = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_name = 'clientes' AND column_name = 'email_lower') AS "Value"
            """).SingleAsync();
        var indice = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM pg_indexes
                           WHERE tablename = 'clientes' AND indexname = 'ix_clientes_empresa_email_lower') AS "Value"
            """).SingleAsync();
        return (coluna, indice);
    }
}
