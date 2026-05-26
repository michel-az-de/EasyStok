using EasyStock.Infra.Postgre.Data;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EasyStock.Infra.Postgre.IntegrationTests;

public sealed class PostgreSqlDatabaseFixture : IAsyncLifetime
{
    // O 'postgres' do container e SUPERUSER e ignora RLS por completo — inclusive
    // FORCE ROW LEVEL SECURITY, que so obriga o DONO da tabela, nunca um superuser.
    // Testes que validam isolamento por tenant precisam de um login comum
    // (NOSUPERUSER, NOBYPASSRLS, nao-dono) sujeito as policies do banco.
    private const string RlsClientRole = "rls_test_client";
    private const string RlsClientPassword = "rls_test_pwd";

    private PostgreSqlContainer? _container;
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("easystock_tests")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _container.StartAsync();
            IsAvailable = true;
            await ResetDatabaseAsync();
        }
        catch (DockerUnavailableException ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    public EasyStockDbContext CreateDbContext()
    {
        if (_container is null) throw new InvalidOperationException("PostgreSQL de teste indisponivel.");

        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new EasyStockDbContext(options);
    }

    /// <summary>
    /// Connection string de um login NOSUPERUSER/NOBYPASSRLS — sujeito as policies
    /// de RLS, ao contrario do 'postgres' usado por <see cref="CreateDbContext"/>.
    /// Pooling desligado: sem o interceptor para emitir RESET, uma conexao reciclada
    /// carregaria o <c>app.empresa_id</c>/<c>app.bypass_rls</c> do teste anterior;
    /// conexao nova a cada uso garante sessao limpa (fail-closed).
    /// </summary>
    public string RlsClientConnectionString
    {
        get
        {
            if (_container is null) throw new InvalidOperationException("PostgreSQL de teste indisponivel.");
            return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
            {
                Username = RlsClientRole,
                Password = RlsClientPassword,
                Pooling = false,
            }.ConnectionString;
        }
    }

    /// <summary>
    /// DbContext conectado como <see cref="RlsClientRole"/> (sujeito a RLS).
    /// Usado pelos RowLevelSecurityTests. Para que um <c>SET app.empresa_id</c>
    /// manual sobreviva ate a query seguinte, o chamador deve abrir a conexao
    /// explicitamente (<c>Database.OpenConnectionAsync</c>) e mante-la pelo escopo.
    /// </summary>
    public EasyStockDbContext CreateRlsClientDbContext()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql(RlsClientConnectionString)
            .Options;

        return new EasyStockDbContext(options);
    }

    public async Task ResetDatabaseAsync()
    {
        if (!IsAvailable || _container is null) return;

        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        await EnsureRlsClientRoleAsync(context);
    }

    /// <summary>
    /// Cria (idempotente) o login NOSUPERUSER e concede DML em todas as tabelas.
    /// Reaplicado a cada reset: <see cref="EnsureDeletedAsync"/> recria o banco e
    /// zera os grants, mas o role e cluster-level e persiste. As migrations rodam
    /// como 'postgres' (dono); este role serve apenas as queries dos testes de RLS.
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
