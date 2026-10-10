using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

/// <summary>
/// #1520 (ADR-0060): as duas migracoes do sync do PWA sobem e descem em PostgreSQL de verdade.
/// <c>AddExclusaoLancamentoCaixaMobile</c> (marcas de exclusao do lancamento) e
/// <c>AddCarimboServidorMobile</c> (carimbo do servidor: coluna com default, backfill e indice).
/// </summary>
public class SyncMobileMigrationsTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Exclusao = "AddExclusaoLancamentoCaixaMobile";
    private const string Carimbo = "AddCarimboServidorMobile";
    private static readonly string[] Tabelas =
        ["mobile_products", "mobile_clients", "mobile_orders", "mobile_batches", "mobile_cash_entries"];

    [SkippableFact]
    public async Task CarimboSobeComBackfillPeloValorQueOPullUsava_EDesceSemPerderLinha()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await using var db = fixture.CreateDbContext();
        var migracoes = db.Database.GetMigrations().ToList();
        var exclusao = migracoes.Single(m => m.EndsWith("_" + Exclusao, StringComparison.Ordinal));
        var carimbo = migracoes.Single(m => m.EndsWith("_" + Carimbo, StringComparison.Ordinal));
        var antesDasDuas = migracoes[migracoes.IndexOf(exclusao) - 1];
        var migrator = db.GetService<IMigrator>();

        // Down das duas: o banco volta a ser o de antes da #1520.
        await migrator.MigrateAsync(antesDasDuas);
        foreach (var tabela in Tabelas)
            (await ColunaExisteAsync(db, tabela, "server_updated_at")).Should().BeFalse($"o Down tira o carimbo de {tabela}");
        (await ColunaExisteAsync(db, "mobile_cash_entries", "deleted_at")).Should().BeFalse();
        (await IndicesDoCarimboAsync(db)).Should().Be(0);

        // Linhas como o banco de antes guardava: hora do aparelho, inclusive adiantada.
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO mobile_products ("Id", "Name", "Category", "Stock", is_custom, is_approved, created_at, updated_at)
            VALUES ('p-{sufixo}', 'Lasanha', 'massa', 3, false, true, now() - interval '9 days', now() - interval '2 days');
            INSERT INTO mobile_clients ("Id", "Name", last_order, order_count, created_at, updated_at)
            VALUES ('c-{sufixo}', 'Ana', now(), 1, now() - interval '9 days', now() - interval '3 days');
            INSERT INTO mobile_orders ("Id", client_snapshot_name, "Status", "Total", created_at, updated_at)
            VALUES ('o-{sufixo}', 'Ana', 'pronto', 10, now() - interval '9 days', now() - interval '4 days');
            INSERT INTO mobile_orders ("Id", client_snapshot_name, "Status", "Total", created_at, updated_at)
            VALUES ('o-futuro-{sufixo}', 'Ana', 'pronto', 10, now(), now() + interval '2 hours');
            INSERT INTO mobile_batches ("Id", "Code", created_at)
            VALUES ('b-{sufixo}', 'LOT-1', now() - interval '5 days');
            INSERT INTO mobile_cash_entries ("Id", "Type", "Amount", "Description", created_at)
            VALUES ('cash-{sufixo}', 'expense', 30, 'Gas', now() - interval '6 days');
            """);

        await migrator.MigrateAsync(carimbo);

        (await EscalarAsync<bool>(db, $"SELECT server_updated_at = updated_at FROM mobile_products WHERE \"Id\" = 'p-{sufixo}'")).Should().BeTrue();
        (await EscalarAsync<bool>(db, $"SELECT server_updated_at = updated_at FROM mobile_clients WHERE \"Id\" = 'c-{sufixo}'")).Should().BeTrue();
        (await EscalarAsync<bool>(db, $"SELECT server_updated_at = updated_at FROM mobile_orders WHERE \"Id\" = 'o-{sufixo}'")).Should().BeTrue();
        (await EscalarAsync<bool>(db, $"SELECT server_updated_at = created_at FROM mobile_batches WHERE \"Id\" = 'b-{sufixo}'")).Should().BeTrue();
        (await EscalarAsync<bool>(db, $"SELECT server_updated_at = created_at FROM mobile_cash_entries WHERE \"Id\" = 'cash-{sufixo}'")).Should().BeTrue();
        (await EscalarAsync<bool>(db, $"SELECT server_updated_at <= now() FROM mobile_orders WHERE \"Id\" = 'o-futuro-{sufixo}'"))
            .Should().BeTrue("hora adiantada do aparelho nao vira carimbo no futuro");
        (await IndicesDoCarimboAsync(db)).Should().Be(5, "um indice (empresa, carimbo) por tabela do pull");
        (await ColunaExisteAsync(db, "mobile_cash_entries", "deleted_at")).Should().BeTrue();

        // Linha gravada por fora do EF recebe o default.
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO mobile_batches ("Id", "Code", created_at) VALUES ('b-novo-{sufixo}', 'LOT-2', now() - interval '1 day');
            """);
        (await EscalarAsync<bool>(db, $"SELECT server_updated_at > now() - interval '1 minute' FROM mobile_batches WHERE \"Id\" = 'b-novo-{sufixo}'"))
            .Should().BeTrue();

        // Down de novo: as colunas saem, as linhas ficam. E sobe outra vez.
        await migrator.MigrateAsync(antesDasDuas);
        (await EscalarAsync<long>(db, $"SELECT count(*) FROM mobile_batches WHERE \"Id\" LIKE '%{sufixo}'")).Should().Be(2);
        (await ColunaExisteAsync(db, "mobile_batches", "server_updated_at")).Should().BeFalse();
        await db.Database.MigrateAsync();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task CarimboEGravadoPeloInterceptor_NaInsercaoEEmTodaAlteracao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await using (var migrar = fixture.CreateDbContext())
            await migrar.Database.MigrateAsync();
        var id = $"p-{Guid.NewGuid():N}"[..20];
        var ontem = DateTime.UtcNow.AddDays(-1);

        // O valor que o codigo pos na entidade (aqui, ontem) nao vale: quem carimba e o servidor.
        await using (var db = ComInterceptor())
        {
            db.Add(new Product { Id = id, Name = "Lasanha", Category = "massa", ServerUpdatedAt = ontem });
            await db.SaveChangesAsync();
        }
        DateTime aoInserir;
        await using (var db = ComInterceptor())
        {
            aoInserir = db.Set<Product>().AsNoTracking().Single(p => p.Id == id).ServerUpdatedAt;
            aoInserir.Should().BeAfter(ontem.AddHours(23));
        }

        await Task.Delay(20);
        await using (var db = ComInterceptor())
        {
            db.Set<Product>().Single(p => p.Id == id).Stock = 7;
            await db.SaveChangesAsync();
        }

        await using (var db = ComInterceptor())
            db.Set<Product>().AsNoTracking().Single(p => p.Id == id).ServerUpdatedAt.Should().BeAfter(aoInserir);
    }

    private EasyStockDbContext ComInterceptor() => new(
        new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(new AuditTimestampsInterceptor())
            .Options);

    private static async Task<T> EscalarAsync<T>(DbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    private static async Task<bool> ColunaExisteAsync(DbContext db, string tabela, string coluna) =>
        await EscalarAsync<long>(db,
            $"SELECT count(*) FROM information_schema.columns WHERE table_name = '{tabela}' AND column_name = '{coluna}'") == 1;

    private static Task<long> IndicesDoCarimboAsync(DbContext db) =>
        EscalarAsync<long>(db, "SELECT count(*) FROM pg_indexes WHERE indexname LIKE 'ix\\_mobile\\_%\\_empresa\\_carimbo'");
}
