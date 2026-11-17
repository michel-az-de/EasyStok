using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

/// <summary>
/// #2813: as marcas do histórico importado entram numa migration única, aditiva e anulável: o pedido que o
/// import quitou (<c>pedidos.quitado_historico_em</c>) e o lançamento de caixa que fica só como arquivo
/// (<c>mobile_cash_entries.nao_promover_em</c>). Sobe e desce em PostgreSQL de verdade; o Down tira só as
/// duas colunas e não perde linha.
/// </summary>
public class MarcasDoHistoricoImportadoMigrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Migracao = "MarcasDoHistoricoImportado";

    [SkippableFact]
    public async Task SobeComAsDuasColunasAnulaveis_EDesceSemPerderLinha()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await using var db = fixture.CreateDbContext();
        var migracoes = db.Database.GetMigrations().ToList();
        var marcas = migracoes.Single(m => m.EndsWith("_" + Migracao, StringComparison.Ordinal));
        var anterior = migracoes[migracoes.IndexOf(marcas) - 1];
        var migrator = db.GetService<IMigrator>();

        // Down: o banco volta a ser o de antes das marcas.
        await migrator.MigrateAsync(anterior);
        (await ColunaAsync(db, "pedidos", "quitado_historico_em")).Should().BeNull("o Down tira a marca do pedido");
        (await ColunaAsync(db, "mobile_cash_entries", "nao_promover_em")).Should().BeNull("o Down tira a marca do lançamento");

        // Lançamento como o banco de antes guardava.
        var id = $"cash-{Guid.NewGuid():N}"[..20];
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO mobile_cash_entries ("Id", "Type", "Amount", "Description", created_at)
            VALUES ({id}, 'expense', 30, 'Gas', now());
            """);

        await migrator.MigrateAsync(marcas);

        // Mesmo tipo da coluna irmã de cada tabela e anulável: a linha antiga fica sem marca, sem backfill.
        var irmaPedido = await ColunaAsync(db, "pedidos", "avaliacao_solicitada_em");
        var irmaLancamento = await ColunaAsync(db, "mobile_cash_entries", "deleted_at");
        irmaPedido.Should().EndWith("/YES");
        irmaLancamento.Should().EndWith("/YES");
        (await ColunaAsync(db, "pedidos", "quitado_historico_em")).Should().Be(irmaPedido);
        (await ColunaAsync(db, "mobile_cash_entries", "nao_promover_em")).Should().Be(irmaLancamento);
        (await ContarAsync(db, $"""SELECT count(*) AS "Value" FROM mobile_cash_entries WHERE "Id" = {id} AND nao_promover_em IS NULL""")).Should().Be(1);
        (await ContarAsync(db, $"""SELECT count(*) AS "Value" FROM pedidos WHERE quitado_historico_em IS NOT NULL""")).Should().Be(0);

        // Down de novo: as colunas saem, a linha fica. E sobe outra vez.
        await migrator.MigrateAsync(anterior);
        (await ColunaAsync(db, "mobile_cash_entries", "nao_promover_em")).Should().BeNull();
        (await ColunaAsync(db, "pedidos", "quitado_historico_em")).Should().BeNull();
        (await ContarAsync(db, $"""SELECT count(*) AS "Value" FROM mobile_cash_entries WHERE "Id" = {id}""")).Should().Be(1);
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public void AsMarcasSoTemSetterPrivado_EntaoNaoHaEscritaPublica()
    {
        typeof(Pedido).GetProperty("QuitadoHistoricoEm")!.SetMethod!.IsPrivate.Should().BeTrue();
        typeof(EasyStock.Domain.Entities.Mobile.CashEntry).GetProperty("NaoPromoverEm")!.SetMethod!.IsPrivate.Should().BeTrue();
    }

    private static Task<string?> ColunaAsync(DbContext db, string tabela, string coluna) =>
        db.Database.SqlQuery<string>($"""
            SELECT data_type || '/' || is_nullable AS "Value"
            FROM information_schema.columns
            WHERE table_name = {tabela} AND column_name = {coluna}
            """).FirstOrDefaultAsync();

    private static Task<int> ContarAsync(DbContext db, FormattableString sql) =>
        db.Database.SqlQuery<int>(sql).SingleAsync();
}
