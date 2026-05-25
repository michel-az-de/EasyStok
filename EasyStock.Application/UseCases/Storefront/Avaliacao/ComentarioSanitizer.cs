using System.Text.RegularExpressions;

namespace EasyStock.Application.UseCases.Storefront.Avaliacao;

/// <summary>
/// Sanitiza comentários de avaliação: strip HTML, trunca em 500 chars,
/// aplica profanity filter básico (false positive ok — melhor que texto bruto vazar).
/// </summary>
public sealed class ComentarioSanitizer
{
    private const int MaxChars = 500;

    private static readonly Regex HtmlTagRegex =
        new(@"<[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] ProfanityList =
    [
        "merda", "porra", "caralho", "foda", "fodase", "puta",
        "viado", "buceta", "cu ", " cu,", " cu.", "piroca",
        "desgraça", "fdp", "filha da puta",
    ];

    /// <summary>
    /// Sanitiza o comentário:
    /// <list type="number">
    ///   <item>Null/vazio → retorna string vazia.</item>
    ///   <item>Remove tags HTML.</item>
    ///   <item>Aplica profanity filter (substitui por ***).</item>
    ///   <item>Trunca em 500 caracteres.</item>
    ///   <item>Trim.</item>
    /// </list>
    /// </summary>
    public string Sanitizar(string? comentario)
    {
        if (string.IsNullOrWhiteSpace(comentario)) return string.Empty;

        var texto = HtmlTagRegex.Replace(comentario, string.Empty);

        foreach (var palavra in ProfanityList)
        {
            texto = Regex.Replace(
                texto,
                Regex.Escape(palavra),
                "***",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        texto = texto.Trim();

        if (texto.Length > MaxChars)
            texto = texto[..MaxChars];

        return texto;
    }
}
