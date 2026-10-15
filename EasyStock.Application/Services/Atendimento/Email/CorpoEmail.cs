using System.Text;
using System.Text.RegularExpressions;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.Email;

/// <summary>
/// Texto do e-mail recebido como o console mostra (#1432): sem o histórico citado (a conversa já tem as
/// mensagens anteriores) e dentro do teto da <see cref="Mensagem"/>. Se cortar tudo (o e-mail era só citação),
/// fica o texto inteiro: melhor sobrar do que sumir.
/// </summary>
public static partial class CorpoEmail
{
    private const string Reticencias = "…";

    public static string? Limpar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        var linhas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var corte = InicioDaCitacao(linhas);
        var proprias = linhas.Take(corte).Where(l => !l.TrimStart().StartsWith('>'));
        var limpo = Compactar(string.Join('\n', proprias));
        if (limpo.Length == 0) limpo = Compactar(string.Join('\n', linhas));
        if (limpo.Length == 0) return null;

        return limpo.Length <= Mensagem.TextoTamanhoMaximo
            ? limpo
            : limpo[..(Mensagem.TextoTamanhoMaximo - Reticencias.Length)].TrimEnd() + Reticencias;
    }

    /// <summary>
    /// Primeira linha do histórico citado: "Em ... escreveu:" (Gmail pt-BR, às vezes quebrado em duas linhas),
    /// "On ... wrote:", "-----Mensagem original-----" e o cabeçalho do Outlook ("De:" seguido de "Enviado:",
    /// "Enviada em:", "Sent:" ou "Data:").
    /// </summary>
    private static int InicioDaCitacao(string[] linhas)
    {
        for (var i = 0; i < linhas.Length; i++)
        {
            var linha = linhas[i].Trim();
            var proxima = i + 1 < linhas.Length ? linhas[i + 1].Trim() : "";

            if (Separador().IsMatch(linha)) return i;
            if (linha.StartsWith("Em ", StringComparison.Ordinal)
                && (linha.EndsWith("escreveu:", StringComparison.Ordinal) || proxima.EndsWith("escreveu:", StringComparison.Ordinal)))
                return i;
            if (linha.StartsWith("On ", StringComparison.Ordinal)
                && (linha.EndsWith("wrote:", StringComparison.Ordinal) || proxima.EndsWith("wrote:", StringComparison.Ordinal)))
                return i;
            if (CabecalhoDe().IsMatch(linha) && CabecalhoSeguinte().IsMatch(proxima)) return i;
            // Outlook põe uma linha de sublinhados antes do "De:".
            if (linha.Length >= 5 && linha.All(c => c == '_') && CabecalhoDe().IsMatch(proxima)) return i;
        }
        return linhas.Length;
    }

    private static string Compactar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        var vazias = 0;
        foreach (var linha in texto.Split('\n'))
        {
            var semFim = linha.TrimEnd();
            if (semFim.Length == 0)
            {
                if (++vazias > 1) continue;
            }
            else vazias = 0;
            sb.Append(semFim).Append('\n');
        }
        return sb.ToString().Trim();
    }

    [GeneratedRegex(@"^-{2,}\s*(Original Message|Mensagem original|Forwarded message|Mensagem encaminhada)\s*-{2,}$", RegexOptions.IgnoreCase)]
    private static partial Regex Separador();

    [GeneratedRegex(@"^(De|From):\s+\S", RegexOptions.IgnoreCase)]
    private static partial Regex CabecalhoDe();

    [GeneratedRegex(@"^(Enviado|Enviada em|Sent|Data|Date):", RegexOptions.IgnoreCase)]
    private static partial Regex CabecalhoSeguinte();
}
