using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EasyStock.Application.UseCases.Etiquetas;

/// <summary>
/// Representação tipada do LayoutJson v=1 para validação.
/// Deserializado antes de chegar ao AbstractValidator.
/// </summary>
public sealed record LayoutJsonDocument(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("size")] LayoutSizeSpec Size,
    [property: JsonPropertyName("elements")] List<LayoutElement> Elements
);

public sealed record LayoutSizeSpec(
    [property: JsonPropertyName("preset")] string Preset,
    [property: JsonPropertyName("w_mm")] decimal WMm,
    [property: JsonPropertyName("h_mm")] decimal HMm,
    [property: JsonPropertyName("orientation")] string Orientation
);

public sealed record LayoutElement(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    // text
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("font")] string? Font,
    [property: JsonPropertyName("size_pt")] decimal? SizePt,
    [property: JsonPropertyName("size_pt_min")] decimal? SizePtMin,
    [property: JsonPropertyName("size_pt_max")] decimal? SizePtMax,
    [property: JsonPropertyName("weight")] int? Weight,
    [property: JsonPropertyName("align")] string? Align,
    [property: JsonPropertyName("overflow")] string? Overflow,
    [property: JsonPropertyName("color")] string? Color,
    // image
    [property: JsonPropertyName("asset")] string? Asset,
    // code
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("quiet_zone_mm")] decimal? QuietZoneMm,
    // nutritional-table
    // position (todos os tipos)
    [property: JsonPropertyName("x_mm")] decimal XMm,
    [property: JsonPropertyName("y_mm")] decimal YMm,
    [property: JsonPropertyName("w_mm")] decimal WMm,
    [property: JsonPropertyName("h_mm")] decimal HMm,
    [property: JsonPropertyName("locked")] bool? Locked
);
