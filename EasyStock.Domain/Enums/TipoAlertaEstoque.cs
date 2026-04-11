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

        EstoqueBaixo = EstoqueCritico,
        ProximoVencimento = ValidadeProxima,
        Reposicao = ReposicaoSugerida
    }
}
