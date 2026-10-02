using System.Security.Cryptography;
using System.Text;

namespace EasyStock.Application.Services.Auth;

/// <summary>
/// Geração e conferência dos segredos de acesso (N8; o convite da N9 reaproveita): link de 32 bytes em base64url,
/// código de 6 dígitos. Só o hash vai ao banco (SHA-256; no código, o hash de <c>{IdDoToken}:{código}</c>, para o mesmo
/// código de duas contas nunca ter o mesmo hash). A comparação do código é em tempo constante.
/// </summary>
public static class SegredosDeAcesso
{
    /// <summary>Tamanho do link em bytes aleatórios (o padrão de <c>JwtTokenService.GerarRefreshToken</c>).</summary>
    public const int BytesDoLink = 32;

    public const int DigitosDoCodigo = 6;

    /// <summary>32 bytes do <see cref="RandomNumberGenerator"/> em base64url, sem preenchimento.</summary>
    public static string GerarLink() => Base64Url(RandomNumberGenerator.GetBytes(BytesDoLink));

    /// <summary>6 dígitos uniformes (<c>GetInt32</c>, sem viés de módulo), com zeros à esquerda.</summary>
    public static string GerarCodigo() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString($"D{DigitosDoCodigo}");

    public static string HashDoLink(string link) => TokenHashHelper.ComputeSha256Hash(link);

    public static string HashDoCodigo(Guid idDoToken, string codigo) =>
        TokenHashHelper.ComputeSha256Hash($"{idDoToken:D}:{codigo}");

    public static bool FormatoDeCodigoValido(string? codigo) =>
        codigo is { Length: DigitosDoCodigo } && codigo.All(char.IsAsciiDigit);

    /// <summary>Compara o hash gravado com o do código informado, em tempo constante.</summary>
    public static bool CodigoConfere(Guid idDoToken, string codigoInformado, string hashGravado) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(HashDoCodigo(idDoToken, codigoInformado)),
            Encoding.ASCII.GetBytes(hashGravado ?? string.Empty));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
