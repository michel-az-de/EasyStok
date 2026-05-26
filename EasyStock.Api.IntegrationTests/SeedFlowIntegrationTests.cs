using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// R6 — Testes do flow startup completo (migrations + seeds) contra Postgres do zero.
///
/// 1) DB vazio + ambiente Production → API sobe, migrations todas aplicadas,
///    SuperAdmin criado, SeedData (demo) NAO roda em prod, NotifTemplates seedados.
/// 2) Startup 3x consecutivo contra o mesmo container → contagens estaveis (idempotencia).
///
/// Aceitamos os custos: container leva ~2s pra subir, factory ~5s. Mas e o unico
/// teste que pega regressao em ordem de migration, [NotMapped] vs schema bootstrap,
/// retry strategy, e fail-fast em Production.
/// </summary>
public sealed class SeedFlowIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;
    private bool _isAvailable;

    private const string JwtSecret = "EasyStock-Seed-Test-SuperSecretKey-Min32Chars!!";
    private const string SuperAdminEmail = "seed-test@easystok.test";
    private const string SuperAdminPassword = "ForteOK!2026XyzZ"; // 16 chars, fora da lista proibida

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_seedflow_tests")
                .WithUsername("postgres")
                // Program.cs:577 falha-rapido em creds default (Username=postgres + Password=postgres).
                .WithPassword("EasyStock-IT-NonDefault-2026!")
                .Build();

            await _pg.StartAsync();
            _isAvailable = true;
        }
        catch (DockerUnavailableException)
        {
            _isAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null) await _pg.DisposeAsync();
    }

    private WebApplicationFactory<Program> CriarFactoryProduction(bool seedDemoData = false)
    {
        if (_pg is null) throw new InvalidOperationException("Container Postgres indisponivel.");

        // SuperAdminSeed le essas env vars do PROCESSO (nao do IConfiguration). Setamos antes da factory.
        Environment.SetEnvironmentVariable("SEED_SUPERADMIN_EMAIL", SuperAdminEmail);
        Environment.SetEnvironmentVariable("SEED_SUPERADMIN_PASSWORD", SuperAdminPassword);
        Environment.SetEnvironmentVariable("SEED_DEMO_DATA", seedDemoData ? "true" : "false");
        // ConfigureAppConfiguration nao chega em tempo p/ Program.cs ler ConnectionStrings
        // antes do AddNpgSql (Program.cs:179) — sem essas env vars, NRE no startup.
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _pg.GetConnectionString());
        Environment.SetEnvironmentVariable("Database__Provider", "PostgreSql");
        Environment.SetEnvironmentVariable("Mobile__ApiKey", "easystock-integration-test-mobile-key-0001");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                b.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:Provider"] = "PostgreSql",
                        ["ConnectionStrings:DefaultConnection"] = _pg.GetConnectionString(),
                        ["ConnectionStrings:Redis"] = "localhost:6379",
                        ["Jwt:Issuer"] = "EasyStock",
                        ["Jwt:Audience"] = "EasyStock",
                        ["Jwt:SecretKey"] = JwtSecret,
                        ["Jwt:ExpirationMinutes"] = "60",
                        ["Anthropic:Enabled"] = "false",
                        ["FileStorage:Provider"] = "Local",
                        // Forca startup a rodar migrations + seeds (default em prod e false).
                        ["RunMigrationsOnStartup"] = "true",
                    });
                });
            });
    }

    private async Task<(int migrations, int perfis, int superAdminUsuarios, int empresas, int notifTemplates)>
        ContarBaselineAsync()
    {
        if (_pg is null) throw new InvalidOperationException();
        await using var conn = new NpgsqlConnection(_pg.GetConnectionString());
        await conn.OpenAsync();

        // Tabelas seguem snake_case (PerfilConfiguration.ToTable("perfis") etc.); colunas
        // mantem PascalCase (EF default). Perfil.Nivel e NivelAcesso enum convertido p/
        // string via HasConversion<string>() — o valor SuperAdmin (=0) e gravado como
        // texto, nao integer; o filtro usa 'SuperAdmin', nao 0.
        var migrations = await ScalarIntAsync(conn, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"");
        var perfis = await ScalarIntAsync(conn, "SELECT COUNT(*) FROM perfis WHERE \"Nivel\" = 'SuperAdmin'");
        var superAdminUsuarios = await ScalarIntAsync(conn,
            "SELECT COUNT(*) FROM usuarios WHERE LOWER(\"Email\") = LOWER(@email)",
            ("email", SuperAdminEmail));
        var empresas = await ScalarIntAsync(conn, "SELECT COUNT(*) FROM empresas");
        var notifTemplates = await ScalarIntAsync(conn, "SELECT COUNT(*) FROM notif_templates");

        return (migrations, perfis, superAdminUsuarios, empresas, notifTemplates);
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection conn, string sql, params (string name, object value)[] parameters)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in parameters)
            cmd.Parameters.AddWithValue(n, v);
        var raw = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(raw);
    }

    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DbVazio_StartupCompleto_AplicaTodasMigrationsESeedSuperAdmin_E_NaoSeedaDemoEmProd()
    {
        if (!_isAvailable) return;

        await using var factory = CriarFactoryProduction(seedDemoData: false);
        // Forca startup
        using var _ = factory.CreateClient();

        var (migrations, perfis, superAdmin, empresas, notifTemplates) = await ContarBaselineAsync();

        migrations.Should().BeGreaterThan(0, "todas migrations EF devem ter rodado");
        perfis.Should().BeGreaterThanOrEqualTo(1, "perfil SuperAdmin global deve existir (Nivel=0)");
        superAdmin.Should().Be(1, "usuario SuperAdmin deve ter sido criado com email das env vars");
        empresas.Should().Be(0, "SeedData (tenants demo) NAO deve rodar em Production sem SEED_DEMO_DATA=true");
        notifTemplates.Should().BeGreaterThan(0, "NotificacoesGlobaisSeed deve ter populado templates globais");
    }

    [Fact]
    public async Task DbVazio_StartupTresVezesConsecutivas_NaoMudaContagem()
    {
        if (!_isAvailable) return;

        // 1a passada
        await using (var f1 = CriarFactoryProduction())
        {
            using var _ = f1.CreateClient();
        }
        var baseline = await ContarBaselineAsync();
        baseline.migrations.Should().BeGreaterThan(0);
        baseline.superAdminUsuarios.Should().Be(1);

        // 2a passada — mesmo container
        await using (var f2 = CriarFactoryProduction())
        {
            using var _ = f2.CreateClient();
        }
        var depois2 = await ContarBaselineAsync();
        depois2.Should().BeEquivalentTo(baseline, "startup 2x deve ser idempotente");

        // 3a passada
        await using (var f3 = CriarFactoryProduction())
        {
            using var _ = f3.CreateClient();
        }
        var depois3 = await ContarBaselineAsync();
        depois3.Should().BeEquivalentTo(baseline, "startup 3x deve ser idempotente");
    }
}
