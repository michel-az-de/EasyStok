namespace EasyStock.Domain.Enums.Storefront;

/// <summary>
/// Linha do item do cardápio (S15, US-024): pronto para servir ou para o cliente finalizar em casa.
/// O pedido guarda o nome em <c>PedidoItem.LinhaSnapshot</c> (camelCase, igual ao contrato público).
/// </summary>
public enum LinhaProduto
{
    ParaServir = 1,
    PrepararEmCasa = 2
}

/// <summary>Formato público/snapshot da <see cref="LinhaProduto"/> ("paraServir" | "prepararEmCasa").</summary>
public static class LinhaProdutoExtensions
{
    public static string ParaContrato(this LinhaProduto linha) => linha switch
    {
        LinhaProduto.PrepararEmCasa => "prepararEmCasa",
        _ => "paraServir"
    };
}
