namespace EasyStock.Application.UseCases.Storefront.Checkout;

/// <summary>Resultado do checkout criado com sucesso (ADR-0014 Fase 3).</summary>
public sealed record CheckoutCriadoDto(
    Guid PedidoId,
    string InitPointUrl,
    int ExpiresIn = 1800);
