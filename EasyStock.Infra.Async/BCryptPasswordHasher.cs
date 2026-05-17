using EasyStock.Application.Ports.Output;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementacao de <see cref="IPasswordHasher"/> usando <c>BCrypt.Net-Next</c>.
/// BCrypt aplica salt aleatorio embutido no hash e custo de trabalho ajustavel
/// (default 11) — adequado para senhas de usuario. Sem dependencias externas
/// (apenas o package gerenciado pela Infra.Async).
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Senha nao pode ser vazia.", nameof(password));
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Hash mal-formado (corrupcao, valor de teste etc.). Tratamos como
            // mismatch silencioso — caller nao precisa diferenciar de senha errada.
            return false;
        }
    }
}
