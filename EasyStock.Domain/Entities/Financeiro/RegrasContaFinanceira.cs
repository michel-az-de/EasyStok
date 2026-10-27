using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Domain.Entities.Financeiro;

/// <summary>
/// Regras comuns a <see cref="ContaPagar"/> e <see cref="ContaReceber"/> (#2095). A parcela não conhece o status da
/// conta; quem decide se a conta aceita dinheiro e se uma parcela pode sumir é o agregado, por aqui.
/// </summary>
internal static class RegrasContaFinanceira
{
    /// <summary>
    /// Só conta emitida e não quitada recebe pagamento. Rascunho ainda não é dívida (e a parcela pode ser removida
    /// depois, levando o pagamento e deixando o movimento do caixa órfão); cancelada não volta a receber.
    /// </summary>
    public static void GarantirAceitaPagamento(StatusContaFinanceira status, string operacao)
    {
        if (status is StatusContaFinanceira.Aberta or StatusContaFinanceira.ParcialmentePaga or StatusContaFinanceira.Vencida)
            return;
        var situacao = status switch
        {
            StatusContaFinanceira.Rascunho => "em Rascunho; emita a conta antes",
            StatusContaFinanceira.Cancelada => "Cancelada",
            StatusContaFinanceira.Paga => "Paga",
            _ => status.ToString()
        };
        throw new RegraDeDominioVioladaException(
            $"Só é possível registrar {operacao} em conta emitida (aberta, parcialmente paga ou vencida). Esta conta está {situacao}.");
    }

    public static void GarantirSemPagamentoConfirmado(decimal valorPago, IEnumerable<PagamentoParcela> pagamentos)
    {
        if (valorPago > 0m || pagamentos.Any(p => p.Status == StatusPagamentoParcela.Confirmado))
            throw new RegraDeDominioVioladaException(
                "Não é possível remover parcela com pagamento confirmado. Estorne o pagamento antes.");
    }
}
