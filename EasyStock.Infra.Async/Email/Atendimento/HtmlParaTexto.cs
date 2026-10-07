using System.Text;
using System.Text.RegularExpressions;
using MimeKit.Text;

namespace EasyStock.Infra.Async.Email.Atendimento;

/// <summary>
/// HTML do e-mail reduzido a texto (#1432). Pelo tokenizador do MimeKit, nunca por regex sobre o HTML: só os dados
/// de texto saem, então script, estilo, imagem e link não chegam ao console (o HTML nunca é renderizado lá). O
/// conteúdo de <c>blockquote</c> é o histórico citado da resposta e fica de fora.
/// </summary>
internal static partial class HtmlParaTexto
{
    private static readonly HashSet<string> Ignorados = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "title", "blockquote", "template", "noscript",
    };

    private static readonly HashSet<string> Blocos = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "tr", "table", "ul", "ol", "li", "h1", "h2", "h3", "h4", "h5", "h6", "section", "article",
        "header", "footer", "pre", "hr",
    };

    public static string Converter(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var sb = new StringBuilder(html.Length / 2);
        var ignorando = 0;
        using var leitor = new StringReader(html);
        var tokenizador = new HtmlTokenizer(leitor) { DecodeCharacterReferences = true };

        while (tokenizador.ReadNextToken(out var token))
        {
            switch (token)
            {
                case HtmlDataToken dados when ignorando == 0 && token.Kind == HtmlTokenKind.Data:
                    var trecho = Espacos().Replace(dados.Data, " ");
                    // Espaço entre tags de bloco (a quebra de linha do código-fonte) não vira linha em branco.
                    if (trecho.Trim().Length > 0 || !NoInicioDeLinha(sb)) sb.Append(trecho);
                    break;

                case HtmlTagToken tag:
                    if (Ignorados.Contains(tag.Name))
                    {
                        if (tag.IsEndTag) ignorando = Math.Max(0, ignorando - 1);
                        else if (!tag.IsEmptyElement) ignorando++;
                        break;
                    }
                    if (ignorando > 0) break;
                    if (string.Equals(tag.Name, "br", StringComparison.OrdinalIgnoreCase)) sb.Append('\n');
                    else if (Blocos.Contains(tag.Name))
                    {
                        // Uma quebra por fronteira de bloco: "</p><div>" não abre linha em branco.
                        if (!NoInicioDeLinha(sb)) sb.Append('\n');
                        if (!tag.IsEndTag && string.Equals(tag.Name, "li", StringComparison.OrdinalIgnoreCase)) sb.Append("- ");
                    }
                    break;
            }
        }

        var linhas = sb.ToString().Split('\n').Select(l => l.Trim());
        return LinhasVazias().Replace(string.Join('\n', linhas), "\n\n").Trim();
    }

    private static bool NoInicioDeLinha(StringBuilder sb) => sb.Length == 0 || sb[^1] == '\n';

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex LinhasVazias();
}
