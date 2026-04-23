namespace EasyStock.Api.Mobile.DTOs;

// Os DTOs abaixo espelham o formato JSON que o app envia.
// Campos opcionais refletem a realidade do client-side.

public record ProductDto(
    string Id,
    string Name,
    string? Emoji,
    string Category,
    string? Unit,
    decimal? Price,
    int Stock,
    bool? Custom
);

public record ClientDto(
    string Id,
    string Name,
    string? Apt,
    string? Address,
    string? Phone,
    long LastOrder,
    int OrderCount
);

public record OrderItemDto(
    string ProductId,
    string Name,
    string? Emoji,
    string? Unit,
    int Qty,
    decimal UnitPrice
);

public record ClientSnapshotDto(string Name, string? Ref);

public record OrderDto(
    string Id,
    string? ClientId,
    ClientSnapshotDto ClientSnapshot,
    List<OrderItemDto> Items,
    string? Notes,
    decimal Total,
    string Status,
    long CreatedAt,
    long UpdatedAt
);

public record BatchItemDto(
    string ProductId,
    string Name,
    string? Emoji,
    string? Unit,
    int Qty,
    string? Photo
);

public record BatchDto(
    string Id,
    string Code,
    List<BatchItemDto> Items,
    string? BatchPhoto,
    long CreatedAt
);

public record CashEntryDto(
    string Id,
    string Type,
    decimal Amount,
    string Description,
    long CreatedAt
);
