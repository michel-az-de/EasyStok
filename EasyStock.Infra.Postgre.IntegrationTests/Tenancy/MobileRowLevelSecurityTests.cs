using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Infra.Postgre.IntegrationTests.Tenancy;

/// <summary>
/// Camada 2 do ADR-0010 no módulo Mobile: as tabelas <c>mobile_*</c> com <c>empresa_id</c> nasceram
/// fora do RLS (isentas pelo prefixo) e o isolamento dependia do <c>Where</c> manual de cada controller;
/// o #1509 registrou vazamento no pull do sync. Com a policy <c>tenant_isolation</c>, uma conexão sem
/// bypass só enxerga a empresa de <c>app.empresa_id</c>.
/// </summary>
public class MobileRowLevelSecurityTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private static readonly string[] TabelasSemeadas = ["mobile_devices", "mobile_products", "mobile_device_commands"];

    [SkippableFact]
    public async Task Tenant_A_ve_so_as_linhas_mobile_de_A()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, _) = await SeedDuasEmpresasComDadosMobileAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await DefinirTenantAsync(ctx, a);

        foreach (var tabela in TabelasSemeadas)
        {
            var sql = $"SELECT DISTINCT empresa_id AS \"Value\" FROM {tabela}";
            var empresas = await ctx.Database.SqlQueryRaw<Guid>(sql).ToListAsync();
            empresas.Should().Equal([a], $"{tabela} não pode devolver linha de outra empresa");
        }
    }

    [SkippableFact]
    public async Task Sem_tenant_as_tabelas_mobile_devolvem_zero_linhas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await SeedDuasEmpresasComDadosMobileAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();

        foreach (var tabela in TabelasSemeadas)
            (await ContarAsync(ctx, tabela)).Should().Be(0, $"{tabela} é fail-closed sem app.empresa_id");
    }

    [SkippableFact]
    public async Task Bypass_ve_as_linhas_mobile_das_duas_empresas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await SeedDuasEmpresasComDadosMobileAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET app.bypass_rls = 'true'");

        foreach (var tabela in TabelasSemeadas)
            (await ContarAsync(ctx, tabela)).Should().Be(2, $"o bypass da allowlist lê {tabela} das duas empresas");
    }

    [SkippableFact]
    public async Task Insert_mobile_em_outra_empresa_e_bloqueado_pelo_WITH_CHECK()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (a, b) = await SeedDuasEmpresasComDadosMobileAsync();

        await using var ctx = fixture.CreateRlsClientDbContext();
        await ctx.Database.OpenConnectionAsync();
        await DefinirTenantAsync(ctx, a);

        var inserirEmB = async () => await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO mobile_device_commands ("Id", device_id, empresa_id, command_type, created_at)
            VALUES ({Guid.NewGuid()}, {"dev-b"}, {b}, {"ping"}, {DateTime.UtcNow})
            """);

        await inserirEmB.Should().ThrowAsync<Npgsql.PostgresException>().Where(ex => ex.SqlState == "42501");
    }

    [SkippableFact]
    public async Task Down_da_migracao_desliga_a_RLS_das_tabelas_mobile()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        var migracoes = db.Database.GetMigrations().ToList();
        var anterior = migracoes[migracoes.FindIndex(m => m.EndsWith("_AddRlsTabelasMobile", StringComparison.Ordinal)) - 1];

        (await ContarMobileComRlsAsync(db)).Should().BeGreaterThan(0, "Up liga a policy nas mobile_ com empresa");

        try
        {
            await migrator.MigrateAsync(anterior);
            (await ContarMobileComRlsAsync(db)).Should().Be(0, "o Down é o rollback e desliga RLS e policy");
        }
        finally
        {
            await fixture.ResetDatabaseAsync();
        }
    }

    private static Task<int> ContarMobileComRlsAsync(DbContext db) => db.Database.SqlQueryRaw<int>("""
        SELECT COUNT(*)::int AS "Value"
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = current_schema() AND c.relkind = 'r' AND c.relname LIKE 'mobile\_%'
          AND (c.relrowsecurity OR EXISTS (SELECT 1 FROM pg_policies p
                                           WHERE p.schemaname = n.nspname AND p.tablename = c.relname
                                             AND p.policyname = 'tenant_isolation'))
        """).FirstAsync();

    private static Task<int> ContarAsync(DbContext ctx, string tabela)
    {
        var sql = $"SELECT COUNT(*)::int AS \"Value\" FROM {tabela}";
        return ctx.Database.SqlQueryRaw<int>(sql).FirstAsync();
    }

    /// <summary>Simula o interceptor: <c>set_config</c> de sessão, como o <c>SET</c> sem LOCAL.</summary>
    private static Task DefinirTenantAsync(DbContext ctx, Guid empresa) =>
        ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.empresa_id', {empresa.ToString()}, false)");

    private async Task<(Guid A, Guid B)> SeedDuasEmpresasComDadosMobileAsync()
    {
        await fixture.ResetDatabaseAsync();

        // Superuser: semeia sem depender de bypass (a policy não se aplica a ele).
        await using var seed = fixture.CreateDbContext();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        seed.Empresas.AddRange(
            new Empresa { Id = a, Nome = "Empresa A", Documento = "11111111111", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow },
            new Empresa { Id = b, Nome = "Empresa B", Documento = "22222222222", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        foreach (var (empresa, sufixo) in new[] { (a, "a"), (b, "b") })
        {
            seed.Set<MobileDevice>().Add(new MobileDevice
            {
                Id = $"dev-{sufixo}", ApiKeyHash = $"hash-{sufixo}", EmpresaId = empresa, LojaId = Guid.NewGuid(),
            });
            seed.Set<Product>().Add(new Product { Id = $"prod-{sufixo}", Name = $"Produto {sufixo}", EmpresaId = empresa });
            seed.Set<DeviceCommand>().Add(new DeviceCommand
            {
                Id = Guid.NewGuid(), DeviceId = $"dev-{sufixo}", EmpresaId = empresa, CommandType = "ping",
            });
        }

        await seed.SaveChangesAsync();
        return (a, b);
    }
}
