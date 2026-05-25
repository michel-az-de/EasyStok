namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando a chamada HTTP ao MercadoPago falha (timeout 5 s ou erro HTTP).
/// Pedido fica em <c>AguardandoPagamento</c> e o background service cancela em 30 min.
/// Mapeada para HTTP 503 no controller.
/// </summary>
public class MercadoPagoIndisponivelException : Exception
{
    public MercadoPagoIndisponivelException(string message)
        : base(message) { }

    public MercadoPagoIndisponivelException(string message, Exception innerException)
        : base(message, innerException) { }
}
