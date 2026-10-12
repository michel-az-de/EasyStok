namespace EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;

public sealed class CheckoutEmAndamentoException() : Exception("Este pedido ainda está sendo processado. Aguarde antes de tentar novamente.");
