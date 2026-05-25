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
    int? DefaultValidityDays = null,
    // C2 (RDC 727/2022): "Avulso" (default) | "Embalado".
    // PWA usa para validar peso obrigatorio em confirmProduction.
    // Inserido 2026-05-16. Lido do Produto(ERP) ligado via ErpProductId.
    string TipoEmbalagem = "Avulso"
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
    long CreatedAt,
    // F7-B — estorno propagado. Web seta EstornadoEm, server reflete pro APK
    // como flag. Mobile aplica como soft-delete visual (a mutation traz o item
    // mas com flag estornado; mobile pode esconder).
    bool? Estornado = null,
    // F7-C — fechamento de caixa pode carregar metodo/categoria. Mobile cria
    // entradas type=income/expense; web tem Metodo (pix/dinheiro/etc) opcional.
    string? Metodo = null
);

/// <summary>
/// F7-C — Fechamento de caixa snapshot. Mobile (cashClosings) ↔ Web
/// (FechamentoCaixa). DTO carrega o sumário consolidado do dia.
/// </summary>
public record CashClosingDto(
    string Id,
    string DateKey,           // "YYYY-MM-DD" — chave do dia
    long ClosedAt,
    string? ClosedByName,
    decimal TotalPagamentosPedidos,
    decimal TotalSaidasExtras,
    decimal SaldoFinal,
    string? Notes
);
