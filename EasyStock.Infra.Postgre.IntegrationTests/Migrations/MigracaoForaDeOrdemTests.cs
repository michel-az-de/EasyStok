using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Migrations;

/// <summary>
/// #1639: migração de carimbo antigo (PR que ficou parada) chegando a um banco que já tem as
/// posteriores. O startup não pode usá-la como alvo: o EF reverteria as posteriores.
/// </summary>
public class MigracaoForaDeOrdemTests
{
    // Só cria um índice: dá para desfazer à mão e simular a migração "ainda não aplicada".
    private const string Antiga = "20261011125001_AddIndiceModeloPendenteAtendimento";
    private const string Indice = "ix_atendimento_mensagens_modelo_pendente";

    [Fact]
    public void SemAplicadasOuSemPendenteAntigaNadaEstaForaDeOrdem()
    {
        MigracoesPendentes.ForaDeOrdem([], ["20260101000000_A"]).Should().BeEmpty("banco novo aplica tudo na ordem");
        MigracoesPendentes.ForaDeOrdem(["20260101000000_A", "20260201000000_B"], ["20260301000000_C"]).Should().BeEmpty();
        MigracoesPendentes.ForaDeOrdem(["20260101000000_A"], []).Should().BeEmpty();
    }

    [Fact]
    public void PendenteAnteriorAUltimaAplicadaEstaForaDeOrdemMesmoComAsAplicadasEmbaralhadas()
    {
        var aplicadas = new[] { "20260301000000_C", "20260101000000_A" };
        var pendentes = new[] { "20260401000000_D", "20260215000000_Velha", "20260110000000_MaisVelha" };

        MigracoesPendentes.ForaDeOrdem(aplicadas, pendentes)
            .Should().Equal("20260110000000_MaisVelha", "20260215000000_Velha");
    }

    [Fact]
    public async Task PendenteDeCarimboAntigoEAplicadaSemReverterAsPosteriores()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new EasyStockDbContext(options);
        await db.Database.MigrateAsync();
        var todas = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        todas.Should().Contain(Antiga);
        var posteriores = todas.Where(id => string.CompareOrdinal(id, Antiga) > 0).ToList();
        posteriores.Should().NotBeEmpty("o cenário precisa de migração aplicada depois da antiga");

        // Produção diante de uma PR antiga: tudo aplicado, menos uma migração de carimbo anterior.
        await DesfazerAAntigaAsync(db);
        var aplicadas = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var pendentes = (await db.Database.GetPendingMigrationsAsync()).ToList();
        pendentes.Should().Equal(Antiga);
        MigracoesPendentes.ForaDeOrdem(aplicadas, pendentes).Should().Equal(Antiga);

        // O que o startup passa a fazer nesse caso: aplicar as pendentes sem alvo.
        await db.Database.MigrateAsync();

        (await db.Database.GetAppliedMigrationsAsync()).Should().Equal(todas, "nenhuma posterior foi revertida");
        (await IndiceExisteAsync(db)).Should().BeTrue("a antiga foi aplicada");

        // O que o startup fazia: migrar PARA a pendente. O EF reverte tudo o que veio depois dela.
        await DesfazerAAntigaAsync(db);
        await db.GetService<IMigrator>().MigrateAsync(Antiga);

        (await db.Database.GetPendingMigrationsAsync()).Should().Equal(posteriores,
            "usar a migração antiga como alvo roda o Down das posteriores: é isso que o startup não pode fazer");
    }

    private static Task DesfazerAAntigaAsync(EasyStockDbContext db) => db.Database.ExecuteSqlRawAsync($"""
        DROP INDEX {Indice};
        DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '{Antiga}';
        """);

    private static async Task<bool> IndiceExisteAsync(EasyStockDbContext db)
    {
        var quantos = await db.Database.SqlQueryRaw<long>($"SELECT count(*) AS \"Value\" FROM pg_indexes WHERE indexname = '{Indice}'").SingleAsync();
        return quantos == 1;
    }
}
