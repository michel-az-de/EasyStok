using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #1330: acabamento do texto que o agente manda ao cliente. O modelo usa travessão mesmo quando o
/// prompt proíbe, então a troca é determinística: entre palavras vira vírgula, no início da linha vira
/// marcador "-", colado em pontuação ou no fim da linha some, e o que sobra vira hífen.
/// </summary>
public static partial class TextoWhatsApp
{
    public static string SemTravessao(string texto)
    {
        if (string.IsNullOrEmpty(texto) || !TemTravessao().IsMatch(texto)) return texto;

        var resultado = InicioDeLinha().Replace(texto, "$1- ");
        resultado = DepoisDePontuacao().Replace(resultado, " ");
        resultado = FimDeLinha().Replace(resultado, "");
        resultado = EntrePalavras().Replace(resultado, ", ");
        return TemTravessao().Replace(resultado, "-");
    }

    [GeneratedRegex("[—–]")]
    private static partial Regex TemTravessao();

    [GeneratedRegex(@"^([ \t]*)[—–][ \t]*", RegexOptions.Multiline)]
    private static partial Regex InicioDeLinha();

    [GeneratedRegex(@"(?<=[.,;:!?])[ \t]*[—–][ \t]*")]
    private static partial Regex DepoisDePontuacao();

    [GeneratedRegex(@"[ \t]*[—–][ \t]*$", RegexOptions.Multiline)]
    private static partial Regex FimDeLinha();

    [GeneratedRegex(@"[ \t]+[—–][ \t]+")]
    private static partial Regex EntrePalavras();
}
