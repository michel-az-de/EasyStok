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
        if ((await FechamentosQueRetratamAsync(caixa, empresaId, instanteUtc, lojaId)).Count > 0)
            throw new UseCaseValidationException(mensagem);
    }

    /// <summary>
    /// Remover ou desfazer um recebimento: só recusa o que o fechamento do dia retratou. Recebimento
    /// lançado depois de o Caixa fechar não entrou no retrato e continua podendo ser desfeito.
    /// </summary>
    public static async Task ExigirRecebimentoForaDoRetratoAsync(ICaixaRepository caixa, Guid empresaId,
        DateTime pagoEmUtc, Guid? lojaId)
    {
        var fechamentos = await FechamentosQueRetratamAsync(caixa, empresaId, pagoEmUtc, lojaId);
        if (fechamentos.Any(f => pagoEmUtc <= f.FechadoEm))
            throw new UseCaseValidationException(RecebimentoEmDiaFechado);
    }

    /// <summary>
    /// #1562: o fechamento sem loja é o da empresa e soma os recebimentos de todas as lojas (Console e Web
    /// fecham assim), então retrata também o pedido que tem loja. O inverso não vale: o fechamento de uma
    /// loja só soma os pedidos dela, e não retrata pedido de outra loja nem pedido sem loja.
    /// </summary>
    private static async Task<IReadOnlyList<FechamentoCaixa>> FechamentosQueRetratamAsync(ICaixaRepository caixa,
        Guid empresaId, DateTime instanteUtc, Guid? lojaId)
    {
        var dia = HorarioBrasil.DataOperacional(instanteUtc);
        var fechamentos = new List<FechamentoCaixa>(2);
        if (await caixa.GetFechamentoDoDiaAsync(empresaId, dia, lojaId) is { } daLoja) fechamentos.Add(daLoja);
        if (lojaId is not null && await caixa.GetFechamentoDoDiaAsync(empresaId, dia, null) is { } daEmpresa)
            fechamentos.Add(daEmpresa);
        return fechamentos;
    }
}
