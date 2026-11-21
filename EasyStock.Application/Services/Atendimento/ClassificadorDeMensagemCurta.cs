using System.Globalization;
using System.Text;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>Tipo de mensagem curta que o Assistente responde sem modelo (#2885).</summary>
public enum TipoMensagemCurta
{
    Nenhuma = 0,
    Saudacao = 1,

    // S76/S77: ainda não são devolvidos pelo classificador.
    Agradecimento = 2,
    PedidoDeCardapio = 3,
}

/// <summary>
/// #2885 (S75): classifica a mensagem inteira, sem "contém". Só entra texto de até 4 palavras e 30 letras, e toda a
/// mensagem precisa ser feita de termos da lista. "sim", "ok", "não", números e qualquer palavra fora da lista ficam em
/// <see cref="TipoMensagemCurta.Nenhuma"/>: são respostas a uma pergunta e pertencem ao modelo.
/// </summary>
public static class ClassificadorDeMensagemCurta
{
    private const int PalavrasMaximo = 4;
    private const int LetrasMaximo = 30;

    private static readonly HashSet<string> Saudacoes = new(StringComparer.Ordinal)
    {
        "oi", "oie", "oii", "oiii", "ola", "eai", "e ai", "bom dia", "boa tarde", "boa noite",
    };

    /// <summary>
    /// Só contam junto de uma saudação ("oi, tudo bem?"). Sozinhos são concordância ("fica para as 15h, tudo bem?" →
    /// "tudo bem"), o mesmo papel de "ok" e "sim", e pertencem ao modelo.
    /// </summary>
    private static readonly HashSet<string> Complementos = new(StringComparer.Ordinal)
    {
        "opa", "tudo bem", "tudo bom", "td bem",
    };

    public static TipoMensagemCurta Classificar(string? texto)
    {
        var palavras = Normalizar(texto);
        if (palavras.Count == 0 || palavras.Count > PalavrasMaximo) return TipoMensagemCurta.Nenhuma;
        if (palavras.Sum(p => p.Length) > LetrasMaximo) return TipoMensagemCurta.Nenhuma;
        return TodasSaudacao(palavras) ? TipoMensagemCurta.Saudacao : TipoMensagemCurta.Nenhuma;
    }

    private static bool TodasSaudacao(IReadOnlyList<string> palavras)
    {
        var temSaudacao = false;
        var i = 0;
        while (i < palavras.Count)
        {
            var par = i + 1 < palavras.Count ? $"{palavras[i]} {palavras[i + 1]}" : null;
            if (par is not null && Saudacoes.Contains(par)) { temSaudacao = true; i += 2; }
            else if (par is not null && Complementos.Contains(par)) i += 2;
            else if (Saudacoes.Contains(palavras[i])) { temSaudacao = true; i++; }
            else if (Complementos.Contains(palavras[i])) i++;
            else return false;
        }
        return temSaudacao;
    }

    /// <summary>Minúsculas, sem acento, sem pontuação, sem emoji e com espaços colapsados.</summary>
    private static List<string> Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];

        var semAcento = texto.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        var limpo = new string(semAcento
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')
            .ToArray());
        return limpo.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
