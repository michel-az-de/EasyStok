namespace EasyStock.Domain.Sales;

/// <summary>
/// #2946: quem a dona chama de volta. Cliente com <see cref="MinimoDeCompras"/> compras ou mais
/// (<see cref="RegraDeCompra"/>), nenhuma nos últimos <see cref="DiasSemCompra"/> dias e sem a tag
/// <see cref="TagConvidada"/> marcada nesse mesmo prazo: o convite não se repete antes de vencer.
/// </summary>
public static class RegraDeRecompra
{
    public const string Relacionamento = "recompra";
    public const int MinimoDeCompras = 2;
    public const int DiasSemCompra = 30;

    /// <summary>Já normalizada como <c>ClienteTag</c> guarda (hífen vira <c>_</c>).</summary>
    public const string TagConvidada = "recompra_convidada";
}
