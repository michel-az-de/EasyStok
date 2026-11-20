using System.Collections.Concurrent;
using Npgsql;
using Testcontainers.PostgreSql;

namespace EasyStock.Testes.Postgres;

/// <summary>
/// Um contêiner Postgres por processo de teste, com bancos modelo migrados uma vez e um clone por
/// classe (#2976, S1.1). Antes cada classe subia o próprio contêiner e rodava as 219 migrações a cada
/// reset; aqui o reset é <c>DROP DATABASE ... WITH (FORCE)</c> e <c>CREATE DATABASE ... TEMPLATE</c>.
/// Arquivo compartilhado por link entre os projetos de integração: cada assembly tem o seu cluster.
/// </summary>
internal sealed class PostgresCluster
{
    private static readonly SemaphoreSlim TravaInicio = new(1, 1);
    private static PostgresCluster? _instancia;

    private readonly PostgreSqlContainer _container;
    private readonly ConcurrentDictionary<string, Lazy<Task>> _modelos = new();

    private PostgresCluster(PostgreSqlContainer container, string imagem, string senha)
    {
        _container = container;
        Imagem = imagem;
        Senha = senha;
    }

    public string Imagem { get; }
    public string Senha { get; }

    /// <summary>Conexão de superusuário ao banco de manutenção <c>postgres</c>.</summary>
    public string ConexaoAdmin => ConexaoDe("postgres");

    /// <summary>
    /// Sobe o contêiner na primeira chamada do processo e devolve sempre o mesmo. Sem Docker, a
    /// <c>DockerUnavailableException</c> sobe para o chamador, que marca o fixture como indisponível.
    /// </summary>
    public static async Task<PostgresCluster> ObterAsync(string imagem, string senha)
    {
        if (_instancia is { } pronto)
            return Conferir(pronto, imagem, senha);

        await TravaInicio.WaitAsync();
        try
        {
            if (_instancia is { } corrida)
                return Conferir(corrida, imagem, senha);

            var container = new PostgreSqlBuilder(imagem)
                .WithDatabase("postgres")
                .WithUsername("postgres")
                .WithPassword(senha)
                // Cada classe abre o próprio pool no mesmo servidor; o padrão de 100 não basta.
                .WithCommand("-c", "max_connections=500")
                .Build();
            await container.StartAsync();
            _instancia = new PostgresCluster(container, imagem, senha);
            return _instancia;
        }
        finally
        {
            TravaInicio.Release();
        }
    }

    private static PostgresCluster Conferir(PostgresCluster cluster, string imagem, string senha) =>
        cluster.Imagem == imagem && cluster.Senha == senha
            ? cluster
            : throw new InvalidOperationException(
                $"O cluster do processo já usa {cluster.Imagem}; um segundo pedido veio com {imagem}.");

    public string ConexaoDe(string banco) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = banco }.ConnectionString;

    /// <summary>
    /// Cria o banco modelo uma vez por processo. Depois de construído, fecha todos os pools e
    /// proíbe conexões (<c>datallowconn=false</c>): clonar um banco com conexão aberta dá 55006.
    /// </summary>
    public Task GarantirModeloAsync(string modelo, Func<string, Task> construir) =>
        _modelos.GetOrAdd(modelo, nome => new Lazy<Task>(() => ConstruirModeloAsync(nome, construir))).Value;

    private async Task ConstruirModeloAsync(string modelo, Func<string, Task> construir)
    {
        await ExecutarAsync($"CREATE DATABASE {Citar(modelo)}");
        await construir(ConexaoDe(modelo));
        NpgsqlConnection.ClearAllPools();
        await ExecutarAsync($"ALTER DATABASE {Citar(modelo)} WITH ALLOW_CONNECTIONS false");
        await ExecutarAsync(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{modelo}' AND pid <> pg_backend_pid()");
    }

    /// <summary>Banco novo, clonado do modelo, com nome único no cluster.</summary>
    public async Task<string> ClonarAsync(string modelo)
    {
        var banco = "t_" + Guid.NewGuid().ToString("N");
        await RecriarAsync(banco, modelo);
        return banco;
    }

    /// <summary>
    /// Devolve o banco ao estado do modelo mantendo o nome, para que a conexão guardada pelo teste
    /// continue válida. Quem mantém host vivo sobre o banco deve pará-lo antes.
    /// </summary>
    public async Task RecriarAsync(string banco, string modelo)
    {
        // Todos os pools: o teste abre o banco também com outro usuário ou outros parâmetros, e a
        // conexão ociosa morta pelo FORCE voltaria do pool com 57P01. Fechar ociosas de outros
        // bancos só custa reabrir.
        NpgsqlConnection.ClearAllPools();
        await ExecutarAsync($"DROP DATABASE IF EXISTS {Citar(banco)} WITH (FORCE)");
        await ExecutarAsync($"CREATE DATABASE {Citar(banco)} TEMPLATE {Citar(modelo)}");
    }

    public async Task DescartarAsync(string banco)
    {
        NpgsqlConnection.ClearAllPools();
        await ExecutarAsync($"DROP DATABASE IF EXISTS {Citar(banco)} WITH (FORCE)");
    }

    /// <summary>
    /// Cria o papel se não existir. Papel é do cluster e não vem no clone (os GRANTs vêm). A trava
    /// consultiva serializa a criação: duas sessões no IF NOT EXISTS dariam 23505 em pg_authid.
    /// </summary>
    public async Task GarantirPapelAsync(string papel, string senha, string atributos)
    {
        await using var conexao = new NpgsqlConnection(ConexaoAdmin);
        await conexao.OpenAsync();
        await using var transacao = await conexao.BeginTransactionAsync();
        await using (var comando = new NpgsqlCommand(
            $"""
            SELECT pg_advisory_xact_lock(hashtext('papel:{papel}'));
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{papel}') THEN
                    CREATE ROLE {Citar(papel)} LOGIN PASSWORD '{senha}' {atributos};
                END IF;
            END $$;
            """, conexao, transacao))
        {
            await comando.ExecuteNonQueryAsync();
        }
        await transacao.CommitAsync();
    }

    /// <summary>Nome de papel único no cluster, para testes que criam o próprio papel.</summary>
    public static string PapelUnico(string prefixo) => $"{prefixo}_{Guid.NewGuid():N}"[..Math.Min(63, prefixo.Length + 33)];

    /// <summary>Remove um papel criado por teste: tira os privilégios dele no banco e apaga o papel.</summary>
    public static async Task RemoverPapelAsync(string conexaoDoBanco, string papel)
    {
        await using var conexao = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(conexaoDoBanco) { Pooling = false }.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand($"DROP OWNED BY {Citar(papel)}; DROP ROLE IF EXISTS {Citar(papel)};", conexao);
        await comando.ExecuteNonQueryAsync();
    }

    private async Task ExecutarAsync(string sql)
    {
        await using var conexao = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConexaoAdmin) { Pooling = false }.ConnectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync();
    }

    private static string Citar(string identificador) => "\"" + identificador.Replace("\"", "\"\"") + "\"";
}
