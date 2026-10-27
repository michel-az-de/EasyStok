using System.Net;
using System.Text.RegularExpressions;

namespace EasyStock.Infra.Async.Email;

/// <summary>
/// #2074: versão texto de um e-mail HTML, para ir junto como multipart/alternative. E-mail só com HTML pontua pior nos
/// filtros de spam e fica ilegível em leitor sem HTML. Não é um conversor completo: cobre o HTML simples dos templates.
/// </summary>
public static partial class TextoDoHtml
{
    public static string Converter(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var texto = BlocoInvisivel().Replace(html, " ");
        texto = Link().Replace(texto, m =>
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value).Trim();
            var rotulo = WebUtility.HtmlDecode(Tag().Replace(m.Groups["texto"].Value, " ")).Trim();
            rotulo = Espacos().Replace(rotulo, " ");
            return string.IsNullOrEmpty(rotulo) || rotulo == href ? href : $"{rotulo}: {href}";
        });
        texto = QuebraDeLinha().Replace(texto, "\n");
        texto = FimDeBloco().Replace(texto, "\n\n");
        texto = FimDeCelula().Replace(texto, " ");
        texto = Tag().Replace(texto, string.Empty);
        texto = WebUtility.HtmlDecode(texto);

        var linhas = texto.Replace("\r", string.Empty).Split('\n')
            .Select(l => Espacos().Replace(l, " ").Trim());
        return MuitasLinhasVazias().Replace(string.Join('\n', linhas), "\n\n").Trim();
    }

    [GeneratedRegex(@"<(head|style|script)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlocoInvisivel();

    [GeneratedRegex("""<a\b[^>]*?href\s*=\s*["'](?<href>[^"']*)["'][^>]*>(?<texto>.*?)</a\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Link();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex QuebraDeLinha();

    [GeneratedRegex(@"</(p|div|h[1-6]|tr|li|table)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex FimDeBloco();

    [GeneratedRegex(@"</t[dh]\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex FimDeCelula();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t\xA0]+")]
    private static partial Regex Espacos();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MuitasLinhasVazias();
}
