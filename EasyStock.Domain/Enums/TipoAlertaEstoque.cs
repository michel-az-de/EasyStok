namespace EasyStock.Domain.Enums
{
    public enum TipoAlertaEstoque
    {
        EstoqueCritico = 1,
        ProdutoParado = 2,
        ValidadeProxima = 3,
        ReposicaoSugerida = 4,
        PedidoAtrasado = 5,
        PedidoRecebido = 6,
        ProdutoVencido = 7,

        /// <summary>#2301: aviso do pipeline de notificações (prazo, caixa, segurança), sem relação com validade.</summary>
        AvisoOperacional = 8,

        EstoqueBaixo = EstoqueCritico,
        ProximoVencimento = ValidadeProxima,
        Reposicao = ReposicaoSugerida
    }
}
