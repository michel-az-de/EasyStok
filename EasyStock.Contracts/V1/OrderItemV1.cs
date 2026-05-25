namespace EasyStock.Contracts.V1;

/// <summary>
/// Item do pedido no contrato V1. Snapshot de produto, preserva nome/preço
/// como estavam no momento da criação (mesmo se Produto for renomeado depois).
/// </summary>
public sealed record OrderItemV1(
    Guid Id,
    Guid OrderId,
    Guid? ProductId,
    string Name,
    string? Emoji,
    string? Unit,
    decimal Quantity,
    MoneyV1 UnitPrice,
    MoneyV1 Subtotal,
    string? Notes,
    DateTime CreatedAt
);
