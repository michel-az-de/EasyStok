namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Porta para hashing/verificacao de senhas. Implementacao concreta vive em
/// <c>EasyStock.Infra.Async</c> (BCrypt). Use cases consomem essa interface
/// em vez de chamar <c>BCrypt.Net.BCrypt.*</c> diretamente — preserva Clean
/// Architecture e permite trocar o algoritmo (Argon2, etc.) sem refatorar
/// caminhos de autenticacao espalhados pela aplicacao.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Gera hash da senha em texto-plano. O salt fica embutido no hash.</summary>
    string Hash(string password);

    /// <summary>
    /// Verifica se <paramref name="password"/> bate com <paramref name="hash"/> previamente
    /// armazenado. Retorna false em qualquer hash invalido ou erro de formato — nao
    /// lanca, para nao expor detalhes ao caller.
    /// </summary>
    bool Verify(string password, string hash);
}
