using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// UM Postgres Testcontainers para todo o assembly, com senha NAO-default (passa o
/// <c>StartupHardening</c>, que recusa <c>Username=postgres + Password=postgres</c>).
/// Compartilhado pelas classes tenant-scoped (isolam dados por GUID proprio). As classes
/// seed-sensiveis (SeedFlow / DemoStore / PostgresApi) mantem container proprio.
///
/// <para>
/// Sem Docker (ex.: dotnet Windows local) o Testcontainers nao sobe: <see cref="Disponivel"/>
/// fica <c>false</c> e os testes SKIPam via <c>Skip.If</c> — nunca passam vazios (falso-verde).
/// No CI/ubuntu (com Docker) o container real sobe uma vez e serve todos.
/// </para>
/// </summary>
public sealed class SharedApiPostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _pg;

    /// <summary>Connection string do container compartilhado (null se Docker indisponivel).</summary>
    public string? ConnString { get; private set; }

    /// <summary>true quando o container subiu — gate do <c>Skip.If</c> nas classes.</summary>
    public bool Disponivel { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine")
                .WithDatabase("easystock_api_it_shared")
                .WithUsername("postgres")
                .WithPassword("EasyStock-IT-NonDefault-2026!")
                .Build();

            await _pg.StartAsync();
            ConnString = _pg.GetConnectionString();
            await AguardarProntoAsync(ConnString);
            Disponivel = true;
        }
        catch (DockerUnavailableException)
        {
            Disponivel = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_pg is not null)
            await _pg.DisposeAsync();
    }

    /// <summary>
    /// Abre conexoes ate o Postgres aceitar, ANTES de a app subir — o
    /// <c>DatabaseProviderResolver</c> faz um probe com timeout curto no startup e falharia
    /// se o container ainda estivesse esquentando (espelha BannerE2ETests / PostgresApi).
    /// </summary>
    private static async Task AguardarProntoAsync(string connectionString)
    {
        for (var tentativa = 0; tentativa < 30; tentativa++)
        {
            try
            {
                await using var conn = new Npgsql.NpgsqlConnection(connectionString);
                await conn.OpenAsync();
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }
}

/// <summary>Collection xUnit que injeta o <see cref="SharedApiPostgresFixture"/> uma vez por assembly.</summary>
[CollectionDefinition("ApiIntegration")]
public sealed class ApiIntegrationCollection : ICollectionFixture<SharedApiPostgresFixture>
{
}
