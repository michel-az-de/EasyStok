namespace EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;

/// <summary>
/// Lançada quando <c>X-Idempotency-Key</c> já existe no banco com um
/// <c>ContentHash</c> diferente: o carrinho foi alterado entre tentativas.
/// O cliente deve gerar um novo UUID antes de retentar.
/// </summary>
public sealed class IdempotencyMismatchException : Exception
{
    public IdempotencyMismatchException()
        : base("Carrinho alterado desde a última tentativa. Gere um novo X-Idempotency-Key e retente.") { }

    public IdempotencyMismatchException(string message) : base(message) { }
}
