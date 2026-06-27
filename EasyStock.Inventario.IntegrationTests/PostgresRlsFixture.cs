using EasyStock.Infra.Postgre.Data;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EasyStock.Inventario.IntegrationTests;

/// <summary>
/// Harness de RLS para o modulo Inventario (Fatia 0). E um hibrido novo, NAO uma
/// copia do PostgreSqlDatabaseFixture: combina a mecanica de role RLS daquele
/// fixture com a resolucao de conexao por env var de PostgresApiIntegrationTests,
/// para rodar TAMBEM no service container do CI. Reusa as fontes canonicas
/// (EasyStockDbContext + migration AddRowLevelSecurity); NAO referencia o projeto
/// EasyStock.Infra.Postgre.IntegrationTests (fora do slnf de CI / bit-rotado).
///
/// Resolucao de conexao:
///   1. EASYSTOCK_IT_PG presente  -> conn string Npgsql (superuser; service container do CI).
///   2. ausente + Docker          -> Testcontainers postgres:16-alpine (efemero, local).
///   3. ausente + sem Docker      -> IsAvailable=false -> testes SKIPam visivel.
///
/// O 'postgres' (superuser) ignora RLS por completo, inclusive FORCE ROW LEVEL
/// SECURITY (FORCE so obriga o DONO da tabela, nunca um superuser). Por isso os
/// testes de isolamento conectam como rls_test_client (NOSUPERUSER, NOBYPASSRLS,
/// nao-dono), o unico login sujeito as policies.
/// </summary>
public sealed class PostgresRlsFixture : IAsyncLifetime
{
    private const string RlsClientRole = "rls_test_client";
    private const string RlsClientPassword = "rls_test_pwd";
    private const string EnvVarName = "EASYSTOCK_IT_PG";

    private PostgreSqlContainer? _container;
    private string? _baseConnectionString;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(external))
        {
            // CI: o service container Postgres ja esta de pe; usamos sua conn string
            // (superuser). Migrations + criacao do role rodam contra ele.
            _baseConnectionString = external;
            IsAvailable = true;
            await ResetDatabaseAsync();
            return;
        }

        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("easystock_inventario_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _container.StartAsync();
            _baseConnectionString = _container.GetConnectionString();
            IsAvailable = true;
            await ResetDatabaseAsync();
        }
        catch (DockerUnavailableException ex)
        {
            IsAvailable = false;
            UnavailableReason = $"Docker indisponivel e {EnvVarName} ausente: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    /// <summary>Conn string do superuser 'postgres' (bypassa RLS). Usada por migrations/seed/reset.</summary>
    private string SuperuserConnectionString =>
        _baseConnectionString ?? throw new InvalidOperationException("PostgreSQL de teste indisponivel.");

    /// <summary>
    /// Conn string do login NOSUPERUSER/NOBYPASSRLS — sujeito as policies RLS.
    /// Pooling desligado: sem interceptor para emitir RESET, uma conexao reciclada
    /// carregaria o app.empresa_id/app.bypass_rls do teste anterior; conexao nova
    /// a cada uso garante sessao limpa (fail-closed).
    /// </summary>
    public string RlsClientConnectionString => new NpgsqlConnectionStringBuilder(SuperuserConnectionString)
    {
        Username = RlsClientRole,
        Password = RlsClientPassword,
        Pooling = false,
    }.ConnectionString;

    /// <summary>DbContext como superuser (bypassa RLS) — migrations/seed/reset.</summary>
    public EasyStockDbContext CreateSuperuserDbContext()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(SuperuserConnectionString)
            .Options;
        return new EasyStockDbContext(options);
    }

    /// <summary>
    /// DbContext como rls_test_client (sujeito a RLS). Para um SET app.empresa_id
    /// manual sobreviver ate a query seguinte, o chamador deve abrir a conexao
    /// (Database.OpenConnectionAsync) e mante-la pelo escopo.
    /// </summary>
    public EasyStockDbContext CreateRlsClientDbContext()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(RlsClientConnectionString)
            .Options;
        return new EasyStockDbContext(options);
    }

    /// <summary>
    /// Reset por DROP SCHEMA (NAO EnsureDeleted). EnsureDeleted faz DROP DATABASE,
    /// que falha (55006: database is being accessed by other users) num service
    /// container compartilhado onde o fixture mantem conexoes ao mesmo banco. DROP
    /// SCHEMA public CASCADE + CREATE recria o schema sem tocar o database; MigrateAsync
    /// reaplica tudo (inclui ENABLE/FORCE RLS + policy em 'produtos'). Mesmo caminho
    /// no Testcontainers e no service container.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        // Precondicao: chamado so com infra disponivel (InitializeAsync apos setar
        // IsAvailable=true; testes apos Skip.If). Sem guard de no-op-skip aqui de
        // proposito (ADR-0023/#394 bane o padrao); se mal-usado, CreateSuperuserDbContext
        // falha alto em vez de virar no-op silencioso.
        await using var ctx = CreateSuperuserDbContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;");
        await ctx.Database.MigrateAsync();
        await EnsureRlsClientRoleAsync(ctx);
    }

    /// <summary>
    /// Cria (idempotente) o login NOSUPERUSER e concede DML. Reaplicado a cada reset:
    /// DROP SCHEMA zera grants dos objetos, mas o role e cluster-level e persiste.
    /// Migrations rodam como superuser (dono); este role serve so as queries RLS.
    /// Interpola apenas const -> o compilador funde em string constante (sem EF1002).
    /// </summary>
    private static async Task EnsureRlsClientRoleAsync(EasyStockDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync($@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{RlsClientRole}') THEN
        CREATE ROLE {RlsClientRole} LOGIN PASSWORD '{RlsClientPassword}' NOSUPERUSER NOBYPASSRLS;
    END IF;
END $$;
GRANT USAGE ON SCHEMA public TO {RlsClientRole};
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {RlsClientRole};
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {RlsClientRole};
");
    }
}
