namespace EasyStock.Contracts.V1;

/// <summary>
/// DTO público do pedido no contrato V1. Estável — não pode mudar sem
/// bumpar pra V2.
///
/// <para>
/// Designed para ser consumido por API endpoints futuros (checkout,
/// marketplace ingest, fiscal callback) e por clients (PWA, mobile, MAUI).
/// O <c>EasyStock.Application</c> mapeia <c>Pedido</c> agregado pra
/// <see cref="OrderV1"/> nas bordas.
/// </para>
///
/// <para>
/// JSON shape é deliberadamente equivalente ao <c>PedidoResult</c> legado
/// (status string lowercase, total como number) pra que adoção seja
/// drop-in nos clients existentes.
/// </para>
/// </summary>
public sealed record OrderV1(
    Guid Id,
    Guid CompanyId,
    Guid? StoreId,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerApt,
    string? CustomerPhone,
    OrderStatusV1 Status,
    MoneyV1 Total,
    MoneyV1 TotalPaid,
    string? Notes,
    string? Origin,
    string? MobileOrderId,
    Guid? SaleId,
    int ItemsCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? DeliveredAt,
    DateTime? CanceledAt
);

/// <summary>
/// Pedido com detalhes 1:N (itens, eventos, pagamentos). Usado em
/// endpoints de detalhe e em fluxos que precisam visão completa.
/// </summary>
public sealed record OrderDetailV1(
    OrderV1 Order,
    IReadOnlyList<OrderItemV1> Items,
    IReadOnlyList<OrderEventV1> Events,
    IReadOnlyList<OrderPaymentV1> Payments
);

public sealed record OrderEventV1(
    Guid Id,
    Guid OrderId,
    string Type,
    string? OldStatus,
    string? NewStatus,
    string? Details,
    Guid? UserId,
    string? UserName,
    string? Origin,
    DateTime OccurredAt
);

public sealed record OrderPaymentV1(
    Guid Id,
    Guid OrderId,
    string Method,
    MoneyV1 Amount,
    string? Reference,
    string? Notes,
    DateTime PaidAt,
    Guid? RegisteredByUserId,
    string? RegisteredByName
);
