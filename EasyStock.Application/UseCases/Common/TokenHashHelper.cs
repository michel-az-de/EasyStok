using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Utilitário para hash determinístico de tokens (refresh token, reset token).
/// Usa SHA-256 em vez de BCrypt porque BCrypt gera salt aleatório a cada chamada,
/// tornando impossível a busca pelo hash armazenado.
/// </summary>
public static class TokenHashHelper
{
    public static string ComputeSha256Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
