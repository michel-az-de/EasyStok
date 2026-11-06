using System.Security.Cryptography;

namespace EasyStock.Application.Services.Auth;

/// <summary>
/// Chave de acesso de uso único (#2201): os 16 bytes da empresa seguidos de 32 bytes aleatórios, em base64url (64
/// caracteres). Só o SHA-256 do texto inteiro vai ao banco (<c>reset_tokens</c>, finalidade <c>Ativacao</c>): trocar a
/// empresa muda o hash e a chave deixa de existir. A empresa no texto só diz em que empresa a sessão abre; quem autoriza é
/// a linha do banco.
/// </summary>
public static class ChavesDeAcesso
{
    public static readonly TimeSpan Validade = TimeSpan.FromDays(30);

    public const string AcaoEmitida = "chave-acesso-emitida";
    public const string AcaoRevogada = "chave-acesso-revogada";
    public const string AcaoUsada = "login-chave-acesso";

    private const int BytesDaEmpresa = 16;
    private const int BytesTotais = BytesDaEmpresa + SegredosDeAcesso.BytesDoLink;

    public static string Gerar(Guid empresaId)
    {
        var bytes = new byte[BytesTotais];
        empresaId.TryWriteBytes(bytes.AsSpan(0, BytesDaEmpresa));
        RandomNumberGenerator.Fill(bytes.AsSpan(BytesDaEmpresa));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Lê a empresa do texto. <c>false</c> para qualquer coisa que não seja uma chave bem formada.</summary>
    public static bool TentarLerEmpresa(string? chave, out Guid empresaId)
    {
        empresaId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(chave) || chave.Length is < 60 or > 70) return false;

        var base64 = chave.Trim().Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        var bytes = new byte[BytesTotais + 3];
        if (!Convert.TryFromBase64String(base64, bytes, out var lidos) || lidos != BytesTotais) return false;

        empresaId = new Guid(bytes.AsSpan(0, BytesDaEmpresa));
        return empresaId != Guid.Empty;
    }

    public static string HashDe(string chave) => TokenHashHelper.ComputeSha256Hash(chave);

    /// <summary>O que pode ir para PR, log e auditoria: os 8 primeiros caracteres do hash, nunca o texto.</summary>
    public static string PrefixoDoHash(string hash) => hash[..8];
}
