namespace EasyStock.Api.IntegrationTests;

/// <summary>
/// Define variaveis de ambiente do processo para um teste e devolve o valor anterior no
/// <see cref="Dispose"/>. Conexao e JWT vao por <c>UseSetting</c> (chega antes de o Program.cs
/// ler a configuracao); aqui ficam so as chaves que a API le do processo, como SEED_* do
/// SuperAdminSeed/SeedData. Sem a devolucao, SEED_DEMO_DATA ou a connection string de um
/// container ja destruido vazavam para as classes que rodam em paralelo (#1890).
/// </summary>
internal sealed class VariaveisDeProcesso : IDisposable
{
    private readonly Dictionary<string, string?> _anteriores = new();

    public VariaveisDeProcesso Definir(string nome, string? valor)
    {
        _anteriores.TryAdd(nome, Environment.GetEnvironmentVariable(nome));
        Environment.SetEnvironmentVariable(nome, valor);
        return this;
    }

    public void Dispose()
    {
        foreach (var (nome, anterior) in _anteriores)
            Environment.SetEnvironmentVariable(nome, anterior);
        _anteriores.Clear();
    }
}
