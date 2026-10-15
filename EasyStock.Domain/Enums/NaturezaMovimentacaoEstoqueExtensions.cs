namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Extensões de <see cref="NaturezaMovimentacaoEstoque"/> ligadas ao guard de
    /// validade (issue #983): por padrão, saída de estoque bloqueia lote vencido
    /// (<see cref="Entities.ItemEstoque.GarantirOperavelParaSaida"/>). Algumas
    /// naturezas existem exatamente para dar baixa/descartar o que já venceu e
    /// precisam furar esse bloqueio.
    /// </summary>
    public static class NaturezaMovimentacaoEstoqueExtensions
    {
        /// <summary>
        /// True quando a natureza representa baixa/descarte de mercadoria — nesse
        /// caso a saída pode ser registrada mesmo com o lote vencido.
        ///
        /// Doacao fica FALSE de propósito: doar produto vencido é ilícito
        /// sanitário. Isso é uma decisão de negócio deliberada, não esquecimento.
        /// </summary>
        public static bool PermiteBaixaDeLoteVencido(this NaturezaMovimentacaoEstoque natureza) => natureza switch
        {
            NaturezaMovimentacaoEstoque.Perda => true,
            NaturezaMovimentacaoEstoque.Prejuizo => true,
            NaturezaMovimentacaoEstoque.Vencimento => true,
            _ => false,
        };
    }
}
