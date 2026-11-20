using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2992: critério determinístico de desistência do cliente sobre o pedido, aplicado ao texto normalizado (sem
/// acento, minúsculas, pontuação virada espaço). "não" antes do verbo invalida a frase. Falsos positivos aceitos:
/// "como cancelar o pedido?" e "pode cancelar o cupom".
/// </summary>
public static class DetectorDesistenciaPedido
{
    private static readonly Regex[] Padroes =
    [
        new(@"\bnao quero mais\s*$", RegexOptions.CultureInvariant),
        new(@"\bnao quero mais (?:(?:o|a|esse|essa|este|esta|meu|minha) )?(?:pedido|compra|encomenda|isso)\b", RegexOptions.CultureInvariant),
        new(@"(?<!\bnao )\bpode cancelar\b", RegexOptions.CultureInvariant),
        new(@"(?<!\bnao )(?<!\bnao quero )\bcancel(?:a|ar|e) (?:o )?pedido\b", RegexOptions.CultureInvariant),
        new(@"(?<!\bnao )\bdesisti\b", RegexOptions.CultureInvariant),
    ];

    public static bool EhDesistencia(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
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
