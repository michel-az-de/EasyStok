namespace EasyStock.Domain.Enums.Storefront;

/// <summary>
/// Como o item do cardápio é conservado até chegar ao cliente (S53): a comanda separa "Preparar em casa" em
/// congelado e refrigerado, e a embalagem segue isso. O pedido guarda o nome em <c>PedidoItem.ConservacaoSnapshot</c>.
/// </summary>
public enum ConservacaoProduto
{
    Ambiente = 0,
    Refrigerado = 1,
    Congelado = 2
}

/// <summary>Formato público/snapshot da <see cref="ConservacaoProduto"/> ("ambiente" | "refrigerado" | "congelado").</summary>
public static class ConservacaoProdutoExtensions
{
    public const string Ambiente = "ambiente";
    public const string Refrigerado = "refrigerado";
    public const string Congelado = "congelado";

    public static string ParaContrato(this ConservacaoProduto conservacao) => conservacao switch
    {
        ConservacaoProduto.Congelado => Congelado,
        ConservacaoProduto.Refrigerado => Refrigerado,
        _ => Ambiente
    };
}
