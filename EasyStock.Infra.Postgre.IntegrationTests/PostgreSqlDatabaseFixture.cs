using EasyStock.Infra.Postgre.Data;
using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using EasyStock.Testes.Postgres;

namespace EasyStock.Infra.Postgre.IntegrationTests;

public sealed class PostgreSqlDatabaseFixture : IAsyncLifetime
{
    // O 'postgres' do container e SUPERUSER e ignora RLS por completo — inclusive
    // FORCE ROW LEVEL SECURITY, que so obriga o DONO da tabela, nunca um superuser.
    // Testes que validam isolamento por tenant precisam de um login comum
    // (NOSUPERUSER, NOBYPASSRLS, nao-dono) sujeito as policies do banco.
    private const string RlsClientRole = "rls_test_client";
    private const string RlsClientPassword = "rls_test_pwd";

    // #2976 (S1.1): um contêiner por processo e um banco modelo migrado uma vez; cada fixture clona
    // o modelo num banco de nome único. Ver EasyStock.Testes.Postgres.PostgresCluster.
    private const string Imagem = "postgres:16-alpine";
    private const string SenhaCluster = "postgres";
    internal const string Modelo = "easystock_template";

    private PostgresCluster? _cluster;
    private string? _banco;
    private string? _conexaoLocal;
    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            // Opt-in exclusivo do laboratório descartável #2184; o reset abaixo nunca aceita outro destino.
            if (Environment.GetEnvironmentVariable("EASYSTOK_2184_TEST_POSTGRES") is { Length: > 0 } local)
            {
                var destino = new NpgsqlConnectionStringBuilder(local);
                if (destino.Host != "127.0.0.1" || destino.Port != 25484 || destino.Database != "easystock_it_2184")
                    throw new InvalidOperationException("O laboratório 2184 só aceita 127.0.0.1:25484/easystock_it_2184.");
                _conexaoLocal = destino.ConnectionString;
                IsAvailable = true;
                await ResetDatabaseAsync();
                return;
            }

            _cluster = await ObterClusterComModeloAsync();
            IsAvailable = true;
            _banco = await _cluster.ClonarAsync(Modelo);
        }
        catch (DockerUnavailableException ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
        }
    }

    /// <summary>
    /// Cluster do processo com o modelo pronto: o papel <see cref="RlsClientRole"/> é criado uma vez,
    /// antes do modelo, e os GRANTs feitos no modelo vêm em cada clone.
    /// </summary>
    internal static async Task<PostgresCluster> ObterClusterComModeloAsync()
    {
        var cluster = await PostgresCluster.ObterAsync(Imagem, SenhaCluster);
        await cluster.GarantirModeloAsync(Modelo, async conexao =>
        {
            await cluster.GarantirPapelAsync(RlsClientRole, RlsClientPassword, "NOSUPERUSER NOBYPASSRLS");
            await using var context = CriarContexto(conexao);
            await context.Database.MigrateAsync();
            await ConcederAoRlsClientAsync(context);
        });
        return cluster;
    }

    public async Task DisposeAsync()
    {
        if (_cluster is not null && _banco is not null)
            await _cluster.DescartarAsync(_banco);
    }

    public EasyStockDbContext CreateDbContext() => CriarContexto(ConnectionString);

    private static EasyStockDbContext CriarContexto(string conexao) =>
        new(new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(conexao).Options);

    /// <summary>
    /// Connection string do superuser 'postgres' (a mesma de <see cref="CreateDbContext"/>) —
    /// bypassa RLS. Usada por testes que sobem o DI de producao
    /// (AddEasyStockPostgreInfrastructure) para validar o wiring dos interceptors.
    /// </summary>
    public string ConnectionString =>
        _conexaoLocal
        ?? (_cluster is not null && _banco is not null ? _cluster.ConexaoDe(_banco) : null)
        ?? throw new InvalidOperationException("PostgreSQL de teste indisponivel.");

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
            return new NpgsqlConnectionStringBuilder(ConnectionString)
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

    /// <summary>
    /// Devolve o banco da classe ao estado recém-migrado: <c>DROP ... WITH (FORCE)</c> e novo clone do
    /// modelo, com o mesmo nome. Teste com host vivo sobre o banco para o host antes de chamar.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        Skip.IfNot(IsAvailable, UnavailableReason ?? "PostgreSQL de teste indisponível.");

        if (_cluster is not null && _banco is not null)
        {
            await _cluster.RecriarAsync(_banco, Modelo);
            return;
        }

        // Laboratório #2184: banco fixo fora do cluster, recriado do zero.
        await using var context = CreateDbContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        await EnsureRlsClientRoleAsync(context);
    }

    /// <summary>Caminho do laboratório #2184: papel idempotente e GRANTs no banco recriado.</summary>
    private static async Task EnsureRlsClientRoleAsync(EasyStockDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync($@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{RlsClientRole}') THEN
        CREATE ROLE {RlsClientRole} LOGIN PASSWORD '{RlsClientPassword}' NOSUPERUSER NOBYPASSRLS;
    END IF;
END $$;");
        await ConcederAoRlsClientAsync(context);
    }

    /// <summary>
    /// DML em todas as tabelas para o papel sujeito a RLS. As migrations rodam como 'postgres' (dono);
    /// este papel serve apenas as queries dos testes de RLS.
    /// </summary>
    private static Task ConcederAoRlsClientAsync(EasyStockDbContext context) =>
        context.Database.ExecuteSqlRawAsync($@"
GRANT USAGE ON SCHEMA public TO {RlsClientRole};
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {RlsClientRole};
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {RlsClientRole};
");
}
