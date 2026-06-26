namespace EasyStock.Api.Validation;

/// <summary>
/// Regras de validacao de cupom na fronteira da API (INV-001 / #463), extraidas do
/// AdminCuponsController para ficarem testaveis em isolamento. Retornam a mensagem
/// de erro (pt-BR) ou null quando valido. O charset do codigo tambem fecha o vetor
/// DOM-XSS no Admin (codigo interpolado em atributo Alpine), mesma classe do #352.
/// </summary>
public static class CupomValidacao
{
    /// <summary>Teto do valor de desconto = precisao da coluna Cupom.Valor decimal(10,2).</summary>
    private const decimal MaxValorDesconto = 99_999_999.99m;

    public static string? ValidarCodigo(string codigoUpper)
    {
        if (codigoUpper.Length is < 3 or > 50)
            return "Codigo do cupom deve ter entre 3 e 50 caracteres.";
        foreach (var c in codigoUpper)
            if (c is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))
                return "Codigo do cupom so pode conter letras, numeros, hifen e underscore.";
        return null;
    }

    public static string? ValidarValor(TipoDesconto? tipo, decimal valor)
    {
        if (valor <= 0)
            return "Valor do desconto deve ser maior que zero.";
        if (tipo == TipoDesconto.Percentual && valor > 100)
            return "Desconto percentual não pode passar de 100%.";
        // Teto = precisao da coluna decimal(10,2). Acima disso o INSERT estourava com
        // DbUpdateException, virando falha silenciosa no Admin (QA ADM-003, issue 693).
        if (valor > MaxValorDesconto)
            return "Valor do desconto é muito alto. Máximo permitido: 99.999.999,99.";
        return null;
    }
}
