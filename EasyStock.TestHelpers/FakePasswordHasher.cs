using EasyStock.Application.Ports.Output;

namespace EasyStock.TestHelpers;

/// <summary>
/// Fake de <see cref="IPasswordHasher"/> para testes unitarios — sem BCrypt
/// real (que custa centenas de ms por chamada e e overkill para testes que
/// so precisam validar o caminho de autenticacao).
///
/// <para>
/// Hash usa o prefixo <c>"hash:"</c> + a senha plain. Verify confere o
/// formato exato. Permite construir SenhaHash deterministico via
/// <see cref="MakeHash(string)"/>.
/// </para>
/// </summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public const string Prefix = "hash:";

    public static string MakeHash(string password) => Prefix + password;

    public string Hash(string password) => MakeHash(password);

    public bool Verify(string password, string hash) =>
        !string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(hash) && hash == MakeHash(password);
}
