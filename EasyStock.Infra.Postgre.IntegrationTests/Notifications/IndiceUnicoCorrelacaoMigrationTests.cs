using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Notifications;

/// <summary>
/// N12: <c>notif_eventos</c> ganha o índice único <c>(EmpresaId, CorrelationId)</c> (migração aditiva
/// <c>AddIndiceUnicoCorrelacaoEvento</c>). É a trava entre dois hosts e a "última execução persistida" das rotinas agendadas.
/// </summary>
public class IndiceUnicoCorrelacaoMigrationTests(PostgreSqlDatabaseFixture fixture) : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string Migracao = "AddIndiceUnicoCorrelacaoEvento";
    private readonly MotorNotificacoesSuporte _s = new(fixture);

    private static EventoNotificacao Evento(Guid empresaId, string correlationId) =>
        EventoNotificacao.Criar(TipoEventoNotificacao.ResumoDiario, empresaId, "{}", correlationId: correlationId);

    private async Task SalvarAsync(params EventoNotificacao[] eventos)
    {
        await using var db = fixture.CreateDbContext();
        using var _ = db.UseRowLevelSecurityBypass();
        db.NotifEventos.AddRange(eventos);
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task MesmaCorrelacaoNaMesmaEmpresaViolaOIndice()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = await _s.SemearEmpresaAsync();
        var chave = $"agenda:{Guid.NewGuid():N}:20261001";
        await SalvarAsync(Evento(empresa, chave));

        var segundo = async () => await SalvarAsync(Evento(empresa, chave));

        var erro = await segundo.Should().ThrowAsync<DbUpdateException>();
        var pg = erro.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        pg.SqlState.Should().Be("23505");
        pg.ConstraintName.Should().Be("ux_notif_eventos_empresa_correlation");
    }

    [SkippableFact]
    public async Task MesmaCorrelacaoEmEmpresasDiferentesPassa()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var a = await _s.SemearEmpresaAsync();
        var b = await _s.SemearEmpresaAsync();
        var chave = $"agenda:{Guid.NewGuid():N}:20261001";

        await SalvarAsync(Evento(a, chave), Evento(b, chave));

        (await _s.LerEventosDaEmpresaAsync(a)).Should().ContainSingle();
        (await _s.LerEventosDaEmpresaAsync(b)).Should().ContainSingle();
    }

    [SkippableFact]
    public async Task MigracaoPreservaEventosExistentes()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        await using var db = fixture.CreateDbContext();
        var migracoes = db.Database.GetMigrations().ToList();
        var alvo = migracoes.Single(m => m.EndsWith("_" + Migracao, StringComparison.Ordinal));
        var anterior = migracoes[migracoes.IndexOf(alvo) - 1];
        var migrator = db.GetService<IMigrator>();

        // Desfaz só o índice (Down) e semeia eventos, inclusive repetidos, como o banco de antes da migração permitia.
        await migrator.MigrateAsync(anterior);
        (await IndiceExisteAsync(db)).Should().BeFalse("o Down remove o índice");
        var empresa = await _s.SemearEmpresaAsync();
        var repetida = $"antiga-{Guid.NewGuid():N}";
        var outros = new[] { Evento(empresa, repetida), Evento(empresa, Guid.NewGuid().ToString("N")) };
        await SalvarAsync(outros);

        await migrator.MigrateAsync(alvo);

        (await IndiceExisteAsync(db)).Should().BeTrue();
        (await _s.LerEventosDaEmpresaAsync(empresa)).Select(e => e.Id).Should().BeEquivalentTo(outros.Select(e => e.Id),
            "os eventos antigos ficam intactos");

        // Down de novo: só o índice sai, os eventos ficam.
        await migrator.MigrateAsync(anterior);
        (await IndiceExisteAsync(db)).Should().BeFalse();
        (await _s.LerEventosDaEmpresaAsync(empresa)).Should().HaveCount(2);
        await migrator.MigrateAsync(alvo);
    }

    private static async Task<bool> IndiceExisteAsync(DbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM pg_indexes WHERE tablename = 'notif_eventos' AND indexname = 'ux_notif_eventos_empresa_correlation'";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }
}
