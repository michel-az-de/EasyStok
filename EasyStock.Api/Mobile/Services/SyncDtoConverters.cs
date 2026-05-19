using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Shared DTO converters and JSON helpers for the mobile sync pipeline.
/// </summary>
internal static class SyncDtoConverters
{
    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static ProductDto ToDto(Product p) =>
        new(p.Id, p.Name, p.Emoji, p.Category, p.Unit, p.Price, p.Stock, p.IsCustom,
            p.Sku, p.DefaultWeightG, p.DefaultValidityDays);

    /// <summary>
    /// Overload C2 (RDC 727/2022): popula TipoEmbalagem sem N+1 no Pull.
    /// </summary>
    internal static ProductDto ToDto(Product p, IReadOnlyDictionary<Guid, TipoEmbalagem> tipoEmbMap)
    {
        var tipo = "Avulso";
        if (p.ErpProductId.HasValue && tipoEmbMap.TryGetValue(p.ErpProductId.Value, out var t))
            tipo = t.ToString();
        return new ProductDto(p.Id, p.Name, p.Emoji, p.Category, p.Unit, p.Price, p.Stock, p.IsCustom,
            p.Sku, p.DefaultWeightG, p.DefaultValidityDays, tipo);
    }

    internal static ClientDto ToDto(Client c) =>
        new(c.Id, c.Name, c.Apt, c.Address, c.Phone,
            new DateTimeOffset(c.LastOrder).ToUnixTimeMilliseconds(), c.OrderCount);

    internal static OrderDto ToDto(Order o) =>
        new(o.Id, o.ClientId,
            new ClientSnapshotDto(o.ClientSnapshotName, o.ClientSnapshotRef),
            o.Items.Select(i => new OrderItemDto(i.ProductId, i.Name, i.Emoji, i.Unit, i.Qty, i.UnitPrice)).ToList(),
            o.Notes, o.Total, o.Status,
            new DateTimeOffset(o.CreatedAt).ToUnixTimeMilliseconds(),
            new DateTimeOffset(o.UpdatedAt).ToUnixTimeMilliseconds(),
            History: o.HistoryJson != null ? TryParseJson(o.HistoryJson) : null,
            ConfirmedBy: o.ConfirmedBy,
            ConfirmedAt: o.ConfirmedAt.HasValue
                ? new DateTimeOffset(o.ConfirmedAt.Value).ToUnixTimeMilliseconds()
                : (long?)null,
            FactAt: o.FactAt.HasValue
                ? new DateTimeOffset(o.FactAt.Value).ToUnixTimeMilliseconds()
                : (long?)null,
            ScheduledDeliveryAt: o.ScheduledDeliveryAt.HasValue
                ? new DateTimeOffset(o.ScheduledDeliveryAt.Value).ToUnixTimeMilliseconds()
                : (long?)null);

    internal static BatchDto ToDto(Batch b) =>
        new(b.Id, b.Code,
            b.Items.Select(i => new BatchItemDto(
                i.ProductId, i.Name, i.Emoji, i.Unit, i.Qty, i.Photo,
                i.WeightG, i.ValidityDays,
                i.ExpiresAt.HasValue
                    ? new DateTimeOffset(i.ExpiresAt.Value).ToUnixTimeMilliseconds()
                    : (long?)null)).ToList(),
            b.BatchPhoto,
            new DateTimeOffset(b.CreatedAt).ToUnixTimeMilliseconds(),
            b.Lote);

    internal static CashEntryDto ToDto(CashEntry c) =>
        new(c.Id, c.Type, c.Amount, c.Description,
            new DateTimeOffset(c.CreatedAt).ToUnixTimeMilliseconds());

    internal static JsonElement Serialize<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    internal static JsonElement? TryParseJson(string json)
    {
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch { return null; }
    }
}
