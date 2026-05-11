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
    bool? Custom,
    // Etiquetas de produção (opcionais — APKs antigos não enviam).
    string? Sku = null,
    int? DefaultWeightG = null,
    int? DefaultValidityDays = null
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
    long UpdatedAt,
    // Campos opcionais (adicionados em versão posterior — defaults pra compat).
    System.Text.Json.JsonElement? History = null,
    string? ConfirmedBy = null,
    long? ConfirmedAt = null,
    long? FactAt = null,
    // F5 — agendamento de pedido (MVP). NULL = pedido pra agora (caso padrão).
    long? ScheduledDeliveryAt = null
);

public record BatchItemDto(
    string ProductId,
    string Name,
    string? Emoji,
    string? Unit,
    int Qty,
    string? Photo,
    // Etiquetas (opcionais — APKs antigos não enviam).
    int? WeightG = null,
    int? ValidityDays = null,
    long? ExpiresAt = null
);

public record BatchDto(
    string Id,
    string Code,
    List<BatchItemDto> Items,
    string? BatchPhoto,
    long CreatedAt,
    // Lote do dia (LOT-YYMMDD). Opcional — APKs antigos omitem.
    string? Lote = null
);

public record CashEntryDto(
    string Id,
    string Type,
    decimal Amount,
    string Description,
    long CreatedAt
);
