namespace EasyStock.Application.UseCases.Caixa;

/// <summary>
/// O teto de uma saída em dinheiro (#1521), numa regra só para o lançamento manual (RegistrarMovimentoCaixa) e para o
/// pagamento de conta a pagar (#2736): em dinheiro, a saída não passa do dinheiro que está na gaveta.
/// </summary>
public static class TetoDaGaveta
{
    /// <summary>Recusa a saída em dinheiro maior que a gaveta do <paramref name="dia"/>.</summary>
    public static void ExigirSaidaEmDinheiroCabe(CaixaDiaResult dia, decimal valor)
    {
        var teto = GavetaCaixa.TetoSaidaEmDinheiro(dia);
        if (valor > teto)
        {
            var ptBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            throw new UseCaseValidationException(
                $"Saída de {valor.ToString("C", ptBr)} em dinheiro é maior que o dinheiro na gaveta " +
                $"({teto.ToString("C", ptBr)}). Pix e cartão não estão na gaveta.");
        }
    }
}
