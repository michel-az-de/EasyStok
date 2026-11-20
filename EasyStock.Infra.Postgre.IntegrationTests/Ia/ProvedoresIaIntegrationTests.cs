using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Ia;

/// <summary>
/// #2957 (S1 de #2948): provedores, modelos e chamadas de IA com Postgres real. A policy de RLS esconde a
/// linha de outra loja, o índice parcial recusa dois modelos padrão, a FK composta recusa modelo apontando
/// para provedor de outra loja, e a migração desce e sobe.
/// </summary>
public class ProvedoresIaIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string MigracaoAnterior = "20261120123520_OrigemDoAnuncio";
    private const string Migracao = "20261120153704_ProvedoresIa";
    private static readonly DateTime Agora = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    private static ProvedorIa Provedor(Guid empresa) =>
        ProvedorIa.Criar(empresa, "DeepSeek", "https://api.deepseek.com/anthropic", AutenticacaoProvedorIa.XApiKey, 20m, Agora);

    private static ModeloIa ModeloLigado(Guid empresa, Guid provedor, string id)
    {
        var m = ModeloIa.Criar(empresa, provedor, id, id, 0.28m, 0.42m, 0.028m, 0.5m, Agora);
        m.Ligar(Agora);
        return m;
    }

    private async Task<ProvedorIa> SalvarProvedorAsync(Guid empresa)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var p = Provedor(empresa);
        db.ProvedoresIa.Add(p);
        db.ChamadasIa.Add(ChamadaIa.Registrar(empresa, p.Id, null, "deepseek-flash", FinalidadeIa.Atendimento,
            null, 10, 5, 0, 0, 0.00001m, 100, DesfechoChamadaIa.Ok, Agora));
        await db.SaveChangesAsync();
        return p;
    }

    [SkippableFact]
    public async Task RlsEscondeProvedorEChamadaDeOutraLoja()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        await SalvarProvedorAsync(empresaA);
        await SalvarProvedorAsync(empresaB);

        // Login comum (NOSUPERUSER, NOBYPASSRLS): só a policy do banco separa as lojas.
        await using var cliente = fixture.CreateRlsClientDbContext();
        await cliente.Database.OpenConnectionAsync();
        await cliente.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresaA.ToString()}, false)");

        foreach (var tabela in new[] { "provedor_ia", "chamada_ia" })
        {
            var deB = await cliente.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*)::int AS \"Value\" FROM {tabela} WHERE empresa_id = {{0}}", empresaB)
                .SingleAsync();
            var deA = await cliente.Database
                .SqlQueryRaw<int>($"SELECT COUNT(*)::int AS \"Value\" FROM {tabela} WHERE empresa_id = {{0}}", empresaA)
                .SingleAsync();
            deB.Should().Be(0, $"{tabela} de outra loja é invisível pela policy tenant_isolation");
            deA.Should().Be(1);
        }
    }

    [SkippableFact]
    public async Task IndiceParcialRecusaDoisModelosPadraoNaMesmaLoja()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresa = Guid.NewGuid();
        var provedor = await SalvarProvedorAsync(empresa);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresa);
        var a = ModeloLigado(empresa, provedor.Id, "deepseek-chat");
        var b = ModeloLigado(empresa, provedor.Id, "deepseek-reasoner");
        a.TornarPadrao(Agora);
        b.TornarPadrao(Agora);
        db.ModelosIa.AddRange(a, b);

        var act = () => db.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Npgsql.PostgresException>()
            .Which.ConstraintName.Should().Be("ux_modelo_ia_padrao_por_empresa");
    }

    [SkippableFact]
    public async Task UmPadraoPorLojaNaoImpedePadraoEmOutraLoja()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        foreach (var empresa in new[] { Guid.NewGuid(), Guid.NewGuid() })
        {
            var provedor = await SalvarProvedorAsync(empresa);
            await using var db = fixture.CreateDbContext();
            db.SetMobileTenantContext(empresa);
            var m = ModeloLigado(empresa, provedor.Id, "deepseek-chat");
            m.TornarPadrao(Agora);
            db.ModelosIa.Add(m);
            await db.SaveChangesAsync();
        }
    }

    [SkippableFact]
    public async Task FkCompostaRecusaModeloQueApontaParaProvedorDeOutraLoja()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        var empresaA = Guid.NewGuid();
        var empresaB = Guid.NewGuid();
        var provedorDeA = await SalvarProvedorAsync(empresaA);

        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaB);
        db.ModelosIa.Add(ModeloLigado(empresaB, provedorDeA.Id, "deepseek-chat"));

        var act = () => db.SaveChangesAsync();
        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<Npgsql.PostgresException>()
            .Which.ConstraintName.Should().Be("fk_modelo_ia_provedor");
    }

    [SkippableFact]
    public async Task MigracaoDesceESobe()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponivel");
        // Banco próprio: descer a migração no banco compartilhado quebraria os outros testes da classe.
        await using var postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new EasyStockDbContext(options);
        await db.Database.MigrateAsync();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(MigracaoAnterior);
        (await ExisteTabelaAsync(db, "provedor_ia")).Should().BeFalse();
        (await ExisteColunaAsync(db, "configuracoes_atendimento", "ModeloIaId")).Should().BeFalse();

        await migrator.MigrateAsync(Migracao);
        (await ExisteTabelaAsync(db, "provedor_ia")).Should().BeTrue();
        (await ExisteColunaAsync(db, "configuracoes_atendimento", "ModeloIaId")).Should().BeTrue();
        var comRls = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM pg_class WHERE relname IN ('provedor_ia','modelo_ia','chamada_ia') AND relrowsecurity AND relforcerowsecurity")
            .SingleAsync();
        comRls.Should().Be(3);
    }

    private static Task<bool> ExisteTabelaAsync(EasyStockDbContext db, string tabela) =>
        db.Database.SqlQueryRaw<bool>("SELECT to_regclass({0}) IS NOT NULL AS \"Value\"", tabela).SingleAsync();

    private static Task<bool> ExisteColunaAsync(EasyStockDbContext db, string tabela, string coluna) =>
        db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = {0} AND column_name = {1}) AS \"Value\"",
            tabela, coluna).SingleAsync();
}
