using System.Security.Cryptography;
using System.Text;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Email;

/// <summary>
/// Id externo das mensagens de e-mail (#1432), único por empresa no banco: é ele que impede o mesmo e-mail de
/// entrar duas vezes. A mensagem principal guarda o Message-ID como veio (sem os sinais de menor e maior), o que
/// a resposta usa no In-Reply-To. Os demais formatos têm prefixo e nunca servem para encadear:
/// <list type="bullet">
///   <item><c>h:</c> Message-ID maior que a coluna (mesmo critério do <c>mid</c> do Instagram);</item>
///   <item><c>sem-id:</c> e-mail sem Message-ID, pelo remetente, id na caixa (UID), assunto e texto: estável entre
///   rodadas, mesmo sem cabeçalho Date;</item>
///   <item><c>anexo:</c> cada anexo, derivado do id da principal.</item>
/// </list>
/// </summary>
public static class IdExternoEmail
{
    private const string PrefixoHash = "h:";
    private const string PrefixoSemId = "sem-id:";
    private const string PrefixoAnexo = "anexo:";

    public static string Principal(string? messageId, string de, string idNaCaixa, string? assunto, string texto)
    {
        var id = messageId?.Trim().Trim('<', '>').Trim();
        if (!string.IsNullOrEmpty(id))
            return id.Length <= Mensagem.ExternoIdTamanhoMaximo ? id : PrefixoHash + Sha256(id);
        return PrefixoSemId + Sha256($"{de}|{idNaCaixa}|{assunto}|{texto}");
    }

    /// <param name="ordem">Posição do anexo no e-mail, a partir de 1.</param>
    public static string Anexo(string principal, int ordem) => PrefixoAnexo + Sha256($"{principal}#{ordem}");

    /// <summary>Só o Message-ID guardado como veio serve para In-Reply-To.</summary>
    public static bool ServeParaEncadear(string? externoId) =>
        !string.IsNullOrWhiteSpace(externoId)
        && !externoId.StartsWith(PrefixoHash, StringComparison.Ordinal)
        && !externoId.StartsWith(PrefixoSemId, StringComparison.Ordinal)
        && !externoId.StartsWith(PrefixoAnexo, StringComparison.Ordinal);

    private static string Sha256(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();
}
