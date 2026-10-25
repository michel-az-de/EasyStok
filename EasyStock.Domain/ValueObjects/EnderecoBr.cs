using System.Globalization;
using System.Text;

namespace EasyStock.Domain.ValueObjects;

/// <summary>
/// #2018: a normalização de CEP e de bairro numa regra só. Antes havia cinco cópias do CEP (frete, checkout, janelas,
/// cálculo de frete, hash do checkout), mais quatro "só dígitos" escritos à mão, e duas do bairro. Frete por CEP e por
/// bairro, janelas de entrega, endereço do pedido e impressão comparam e mostram o mesmo CEP.
/// </summary>
public static class EnderecoBr
{
    /// <summary>Um CEP tem 8 dígitos.</summary>
    public const int TamanhoDoCep = 8;

    /// <summary>Só os dígitos: aceita "05500-000", " 05500000 " e "05500000". Nulo ou sem dígito vira vazio.</summary>
    public static string NormalizarCep(string? cep)
    {
        if (string.IsNullOrWhiteSpace(cep)) return string.Empty;
        var sb = new StringBuilder(cep.Length);
        foreach (var c in cep)
        {
            if (char.IsDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>True quando, tirada a máscara, sobram os 8 dígitos do CEP.</summary>
    public static bool CepValido(string? cep) => NormalizarCep(cep).Length == TamanhoDoCep;

    /// <summary>"05500000" vira "05500-000"; o que não tem 8 dígitos volta como veio (aparado).</summary>
    public static string FormatarCep(string? cep)
    {
        var digitos = NormalizarCep(cep);
        return digitos.Length == TamanhoDoCep ? $"{digitos[..5]}-{digitos[5..]}" : (cep ?? string.Empty).Trim();
    }

    /// <summary>
    /// Minúsculas, sem acento e sem espaço nas pontas, para comparar bairro (e número ou complemento) sem depender de
    /// como foi digitado. "Butantã" vira "butanta".
    /// </summary>
    public static string NormalizarBairro(string? bairro)
    {
        var formD = (bairro ?? string.Empty).Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
