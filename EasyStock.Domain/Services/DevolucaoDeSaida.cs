namespace EasyStock.Domain.Services;

/// <summary>
/// A regra única de devolver uma saída ao estoque (#2003, estoque-17): o estorno manual, o cancelamento do
/// pedido, o cancelamento do PWA e a exclusão do lote do PWA faziam cada um a sua cópia.
/// </summary>
public static class DevolucaoDeSaida
{
    /// <summary>
    /// Devolve <paramref name="saida"/> ao <paramref name="lote"/>: a parte que virou descoberto é abatida
    /// primeiro e só o excedente volta ao saldo, a saída fica marcada como estornada (não volta duas vezes)
    /// e o estorno nasce com o mesmo documento da saída. <paramref name="lote"/> nulo (lote apagado) só marca e
    /// estorna a movimentação.
    /// </summary>
    /// <returns>A movimentação de estorno, para quem chama gravar.</returns>
    public static MovimentacaoEstoque Aplicar(
        MovimentacaoEstoque saida,
        ItemEstoque? lote,
        DateTime agora,
        string? descricao,
        string? motivoEstorno = null,
        AuditoriaContexto? auditoria = null)
    {
        lote?.RestaurarSaidaEstornada(saida.Quantidade, agora);
        saida.MarcarComoEstornada(agora);
        return MovimentacaoEstoque.CriarEstorno(Guid.NewGuid(), saida, agora, descricao, agora, motivoEstorno, auditoria);
    }
}
