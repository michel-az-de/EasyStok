using System.Security.Cryptography;
using EasyStock.Application.Ports.Output.Fiscal;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Gera o cNF (8 dígitos) usado na chave de acesso. Usa
/// <see cref="RandomNumberGenerator"/> para garantir entropia
/// criptográfica adequada — evita colisão entre caixas concorrentes.
/// </summary>
public sealed class GeradorChaveAcesso : IGeradorChaveAcesso
{
    public string GerarCodigoNumerico8()
    {
        // RandomNumberGenerator.GetInt32 é cryptographically secure e enviesa.
        // 0..99_999_999 cobre os 8 dígitos.
        var n = RandomNumberGenerator.GetInt32(0, 100_000_000);
        return n.ToString("D8");
    }
}
