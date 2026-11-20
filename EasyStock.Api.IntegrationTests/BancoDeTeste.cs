using EasyStock.Infra.Postgre.Data;
using EasyStock.Testes.Postgres;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// Banco de teste clonado do modelo migrado (#2976, S1.4), no lugar do <c>PostgreSqlContainer</c> que
/// cada classe subia. Mantém a superfície usada pelos testes (<c>StartAsync</c>,
/// <c>GetConnectionString</c>, <c>DisposeAsync</c>). Sem Docker, <c>StartAsync</c> lança a mesma
/// <c>DockerUnavailableException</c> de antes.
/// <para>
/// O modelo leva só as migrações, pelo mesmo <c>MigrateAsync</c> do <c>--migrate-only</c>. O seed segue
/// no startup de cada factory, como antes do clone: o ambiente varia por classe (SeedFlow sobe em
/// Production, OtpStubFlag escolhe o ambiente) e o seed da demo só roda em Development.
/// </para>
/// </summary>
public sealed class BancoDeTeste : IAsyncDisposable
{
    private const string Imagem = "postgres:17-alpine";
    // O StartupHardening recusa a dupla postgres/postgres (#1890); o cluster é descartável.
    private const string Senha = "senha-de-teste-descartavel";
    private const string Modelo = "easystock_api_template";

    private PostgresCluster? _cluster;
    private string? _banco;

    public async Task StartAsync()
    {
        var cluster = await PostgresCluster.ObterAsync(Imagem, Senha);
        await cluster.GarantirModeloAsync(Modelo, async conexao =>
        {
            await using var context = new EasyStockDbContext(
                new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(conexao).Options);
            await context.Database.MigrateAsync();
        });
        _banco = await cluster.ClonarAsync(Modelo);
        _cluster = cluster;
    }

    public string GetConnectionString() =>
        _cluster is not null && _banco is not null
            ? _cluster.ConexaoDe(_banco)
            : throw new InvalidOperationException("Banco de teste não iniciado.");

    /// <summary>Conexão de superusuário ao banco <c>postgres</c> do cluster, para criar papel de teste.</summary>
    public string ConexaoAdmin => _cluster?.ConexaoAdmin ?? throw new InvalidOperationException("Banco de teste não iniciado.");

    public async ValueTask DisposeAsync()
    {
        if (_cluster is not null && _banco is not null)
            await _cluster.DescartarAsync(_banco);
    }
}
