using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2992: critério determinístico de desistência do cliente sobre o pedido, aplicado ao texto normalizado (sem
/// acento, minúsculas, pontuação virada espaço). "não" antes do verbo invalida a frase. Pergunta não conta,
/// "pode cancelar" seguido de outro item (a bebida, o outro sabor) não conta e "desisti de ..." também não.
/// </summary>
public static class DetectorDesistenciaPedido
{
    private static readonly Regex[] Padroes =
    [
        new(@"\bnao quero mais\s*$", RegexOptions.CultureInvariant),
        new(@"\bnao quero mais (?:(?:o|a|esse|essa|este|esta|meu|minha) )?(?:pedido|compra|encomenda|isso)\b", RegexOptions.CultureInvariant),
        new(@"(?<!\bnao )\bpode cancelar\b(?! (?:o|a|os|as|um|uma|esse|essa|meu|minha) (?!pedido|compra|encomenda))", RegexOptions.CultureInvariant),
        new(@"(?<!\bnao )(?<!\bnao quero )(?<!\bnao vou )\bcancel(?:a|ar|e) (?:o )?pedido\b", RegexOptions.CultureInvariant),
        new(@"(?<!\bnao )\bdesisti\b(?! de\b)", RegexOptions.CultureInvariant),
    ];

    public static bool EhDesistencia(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Contains('?')) return false;
        var normalizado = Normalizar(texto);
        return Padroes.Any(padrao => padrao.IsMatch(normalizado));
    }

    private static string Normalizar(string texto)
    {
        var semAcento = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(c);
        return Regex.Replace(semAcento.ToString().ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim();
    }
}
