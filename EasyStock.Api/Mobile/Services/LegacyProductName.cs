using System.Text.RegularExpressions;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// #2813: na primeira carga o produto do ERP e nome + embalagem. A embalagem e o campo "unit" do cadastro do PWA antigo
/// ("300g", "250ml"); "un" e o padrao do app e nao conta como embalagem. O nome composto e a unica chave do produto: a uniao
/// de repetidos, a colisao com o ERP e o cadastro novo usam este mesmo texto. Assim dois registros so se unem quando dao o
/// mesmo nome no ERP (sem diferenciar maiusculas), e a importacao nunca pede ao ERP dois produtos com o mesmo nome.
/// Formato: "nome embalagem". O nome perde so os espacos das pontas e mantem a caixa digitada. A embalagem vai em minusculas,
/// com espacos colapsados e o numero separado da medida ("300G" vira "300 g"). Sem embalagem, ou com a unidade generica,
/// fica so o nome. Se o nome ja traz a embalagem, ela nao se repete.
/// </summary>
public static class LegacyProductName
{
    private static readonly string[] GenericUnits = ["un", "und", "unid", "unidade"];
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex NumberAndMeasure = new(@"^(\d+(?:[.,]\d+)?)\s*(\p{L}+)$", RegexOptions.CultureInvariant);
    private static readonly Regex NumberBeforeLetter = new(@"(?<=\d)(?=\p{L})", RegexOptions.CultureInvariant);

    public static string Compose(string name, string? packaging)
    {
        var plain = name.Trim();
        var normalized = NormalizePackaging(packaging);
        return normalized.Length == 0 || Contains(plain, normalized) ? plain : plain + " " + normalized;
    }

    private static string NormalizePackaging(string? packaging)
    {
        var text = Whitespace.Replace((packaging ?? "").Trim(), " ").ToLowerInvariant();
        if (GenericUnits.Contains(text, StringComparer.Ordinal)) return "";
        var measure = NumberAndMeasure.Match(text);
        return measure.Success ? measure.Groups[1].Value + " " + measure.Groups[2].Value : text;
    }

    /// <summary>Palavra inteira, sem diferenciar caixa nem o espaco entre numero e medida: "600g" esta em "Lasanha 600 g", "600" nao esta em "1600 g".</summary>
    private static bool Contains(string name, string packaging) =>
        (" " + Comparable(name) + " ").Contains(" " + Comparable(packaging) + " ", StringComparison.Ordinal);

    private static string Comparable(string text) =>
        Whitespace.Replace(NumberBeforeLetter.Replace(text, " "), " ").Trim().ToLowerInvariant();
}
