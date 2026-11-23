namespace EasyStock.Domain.Sales;

/// <summary>
/// #3244: o estágio do cliente pela quantidade de compras (<see cref="RegraDeCompra"/>). A API calcula e o Console só desenha.
/// "Sem pedido" é o termo da #3193, não "Lead".
/// </summary>
public static class RegraDeEstagio
{
    public const string SemPedido = "sem_pedido";
    public const string PrimeiraCompra = "primeira_compra";
    public const string Recorrente = "recorrente";
    public const string Fiel = "fiel";

    public const int MinimoDeRecorrente = 2;
    public const int MinimoDeFiel = 5;

    public static string De(int compras) => compras switch
    {
        <= 0 => SemPedido,
        1 => PrimeiraCompra,
        < MinimoDeFiel => Recorrente,
        _ => Fiel,
    };
}
