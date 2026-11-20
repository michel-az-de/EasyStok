using DotNet.Testcontainers.Builders;
using EasyStock.Testes.Postgres;
using FluentAssertions;
using Npgsql;

namespace EasyStock.Infra.Postgre.IntegrationTests.Cluster;

/// <summary>
/// Aceite 5 da E1 (#2976): o harness do banco modelo aguenta clones seguidos, reset do mesmo banco,
/// criação concorrente do mesmo papel e nova tentativa do modelo após falha, os pontos onde o
/// Postgres dá 55006 ou 23505 ou onde uma falha ficaria em cache.
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

    [SkippableFact]
    public async Task Modelo_que_falhou_ao_construir_e_reconstruido_no_pedido_seguinte()
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

        var modelo = "m_" + Guid.NewGuid().ToString("N");
        var falha = () => cluster.GarantirModeloAsync(modelo, _ => throw new InvalidOperationException("falha simulada"));
        await falha.Should().ThrowAsync<InvalidOperationException>();

        // A falha não fica em cache nem deixa o banco meio criado: o segundo pedido constrói do zero.
        await cluster.GarantirModeloAsync(modelo, async conexao =>
        {
            await using var c = new NpgsqlConnection(conexao);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE TABLE marca (id int)", c);
            await cmd.ExecuteNonQueryAsync();
        });
        var clone = await cluster.ClonarAsync(modelo);
        try
        {
            await using var c = new NpgsqlConnection(cluster.ConexaoDe(clone));
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM marca", c);
            ((long)(await cmd.ExecuteScalarAsync())!).Should().Be(0);
        }
        finally
        {
            await cluster.DescartarAsync(clone);
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
