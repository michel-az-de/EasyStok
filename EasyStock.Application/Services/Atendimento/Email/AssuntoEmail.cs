using System.Text.RegularExpressions;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Email;

/// <summary>Assunto do e-mail no atendimento (#1432): a conversa guarda o assunto limpo; a resposta sai com "Re:".</summary>
public static partial class AssuntoEmail
{
    /// <summary>Assunto de quando a conversa não tem assunto (o e-mail chegou sem, ou a conversa não veio de e-mail).</summary>
    public const string Padrao = "Mensagem da loja";

    /// <summary>Tira "Re:", "RES:", "Fwd:", "ENC:" e afins, repetidos, do começo. Vazio vira nulo.</summary>
    public static string? SemPrefixos(string? assunto)
    {
        var atual = Mensagem.NormalizarAssunto(assunto);
        while (atual is not null)
        {
            var sem = Prefixo().Replace(atual, "", 1).Trim();
            if (sem.Length == atual.Length) break;
            atual = sem.Length == 0 ? null : sem;
        }
        return atual;
    }

    /// <summary>"Re: assunto" para a resposta da loja; sem assunto, <see cref="Padrao"/>.</summary>
    public static string Resposta(string? assuntoDaConversa) =>
        SemPrefixos(assuntoDaConversa) is { } limpo ? $"Re: {limpo}" : Padrao;

    // re, res (Outlook pt-BR), fw, fwd, enc/encaminhado, tr (fr), aw/wg (de); com contador "[2]" opcional.
    [GeneratedRegex(@"^\s*(re|res|fw|fwd|enc|encaminhado|tr|aw|wg)\s*(\[\d+\])?\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Prefixo();
}
