namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando a dona fechou a loja na mão (S40, <c>ControleManualLoja.ForcarFechada</c>) e o
/// site tenta criar um pedido. Mapeada para HTTP 409; a mensagem é a de "loja fechada" configurada.
/// </summary>
public class LojaFechadaException : RegraDeDominioVioladaException
{
    public LojaFechadaException()
        : base("A loja não está recebendo pedidos agora.")
    {
    }

    public LojaFechadaException(string message)
        : base(message)
    {
    }

    public LojaFechadaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
