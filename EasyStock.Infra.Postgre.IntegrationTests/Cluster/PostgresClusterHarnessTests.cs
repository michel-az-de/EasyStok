using DotNet.Testcontainers.Builders;
using EasyStock.Testes.Postgres;
using FluentAssertions;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Cluster;

/// <summary>
/// Aceite 5 da E1 (#2976): o harness do banco modelo aguenta clones seguidos, reset do mesmo banco
/// e criação concorrente do mesmo papel, os três pontos onde o Postgres dá 55006 ou 23505.
/// </summary>
public sealed class PostgresClusterHarnessTests
{
    [SkippableFact]
    public async Task Clona_o_modelo_duas_vezes_seguidas_e_cria_o_papel_em_paralelo()
    {
        PostgresCluster cluster;
        try
        {
            cluster = await PostgreSqlDatabaseFixture.ObterClusterComModeloAsync();
        }
        catch (DockerUnavailableException ex)
        {
            throw new SkipException("Docker indisponível: " + ex.Message);
        }

        var primeiro = await cluster.ClonarAsync(PostgreSqlDatabaseFixture.Modelo);
        var segundo = await cluster.ClonarAsync(PostgreSqlDatabaseFixture.Modelo);
        var papel = PostgresCluster.PapelUnico("harness");
        try
        {
            var migracoesDoModelo = await ContarMigracoesAsync(cluster, primeiro);
            migracoesDoModelo.Should().BeGreaterThan(200, "o clone vem com o schema inteiro do modelo");
            (await ContarMigracoesAsync(cluster, segundo)).Should().Be(migracoesDoModelo);

            // Reset duas vezes seguidas com uma conexão do pool ainda aberta no banco.
            await using (var aberta = new NpgsqlConnection(cluster.ConexaoDe(primeiro)))
            {
                await aberta.OpenAsync();
                await cluster.RecriarAsync(primeiro, PostgreSqlDatabaseFixture.Modelo);
            }
            await cluster.RecriarAsync(primeiro, PostgreSqlDatabaseFixture.Modelo);
            (await ContarMigracoesAsync(cluster, primeiro)).Should().Be(migracoesDoModelo);

            await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => cluster.GarantirPapelAsync(papel, "harness_pwd", "NOSUPERUSER NOBYPASSRLS")));

            await using var admin = new NpgsqlConnection(cluster.ConexaoAdmin);
            await admin.OpenAsync();
            await using var conta = new NpgsqlCommand($"SELECT count(*) FROM pg_roles WHERE rolname = '{papel}'", admin);
            ((long)(await conta.ExecuteScalarAsync())!).Should().Be(1);
        }
        finally
        {
            await PostgresCluster.RemoverPapelAsync(cluster.ConexaoDe(primeiro), papel);
            await cluster.DescartarAsync(primeiro);
            await cluster.DescartarAsync(segundo);
        }
    }

    private static async Task<long> ContarMigracoesAsync(PostgresCluster cluster, string banco)
    {
        await using var conexao = new NpgsqlConnection(cluster.ConexaoDe(banco));
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\"", conexao);
        return (long)(await comando.ExecuteScalarAsync())!;
    }
}
