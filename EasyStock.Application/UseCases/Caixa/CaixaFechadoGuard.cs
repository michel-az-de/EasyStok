namespace EasyStock.Application.UseCases.Caixa;

/// <summary>
/// Guarda única do "dia de caixa já fechado" para operações que mexem no dinheiro de um pedido
/// (#1547). O fechamento é um retrato do dia: devolver, remover ou desfazer recebimento num dia
/// retratado faria o retrato divergir do que o Caixa soma ao vivo.
/// </summary>
public static class CaixaFechadoGuard
{
    public const string RecebimentoEmDiaFechado =
        "O Caixa do dia deste recebimento já foi fechado. O pagamento não foi removido: registre uma devolução.";

    /// <param name="instanteUtc">Instante cujo dia operacional (Brasília) recebe o efeito da operação.</param>
    public static async Task ExigirDiaAbertoAsync(ICaixaRepository caixa, Guid empresaId, DateTime instanteUtc,
        Guid? lojaId, string mensagem)
    {
        if (await caixa.GetFechamentoDoDiaAsync(empresaId, HorarioBrasil.DataOperacional(instanteUtc), lojaId) is not null)
            throw new UseCaseValidationException(mensagem);
    }

    /// <summary>
    /// Remover ou desfazer um recebimento: só recusa o que o fechamento do dia retratou. Recebimento
    /// lançado depois de o Caixa fechar não entrou no retrato e continua podendo ser desfeito.
    /// </summary>
    public static async Task ExigirRecebimentoForaDoRetratoAsync(ICaixaRepository caixa, Guid empresaId,
        DateTime pagoEmUtc, Guid? lojaId)
    {
        var fechamento = await caixa.GetFechamentoDoDiaAsync(empresaId, HorarioBrasil.DataOperacional(pagoEmUtc), lojaId);
        if (fechamento is not null && pagoEmUtc <= fechamento.FechadoEm)
            throw new UseCaseValidationException(RecebimentoEmDiaFechado);
    }
}
