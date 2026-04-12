using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Web.Models.Api;

public record ProdutoResumo
{
    public Guid Id { get; init; }
    public string Nome { get; init; } = string.Empty;
    public SkuBaseDto? SkuBase { get; init; }
    public Guid CategoriaId { get; init; }
    public DinheiroDto? PrecoReferencia { get; init; }
    public DinheiroDto? CustoReferencia { get; init; }
    public string? Marca { get; init; }
    [JsonConverter(typeof(EnumStringOrIntConverter))]
    public int Status { get; init; }

    public string StatusNome => Status == 0 ? "Ativo" : "Inativo";
}

[JsonConverter(typeof(SkuBaseDtoJsonConverter))]
public record SkuBaseDto
{
    public string Value { get; init; } = string.Empty;
}

internal sealed class SkuBaseDtoJsonConverter : JsonConverter<SkuBaseDto>
{
    public override SkuBaseDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String)
            return new SkuBaseDto { Value = reader.GetString() ?? string.Empty };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            string val = string.Empty;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.GetString()!.Equals("value", StringComparison.OrdinalIgnoreCase))
                {
                    reader.Read();
                    val = reader.GetString() ?? string.Empty;
                }
            }
            return new SkuBaseDto { Value = val };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for SkuBaseDto.");
    }

    public override void Write(Utf8JsonWriter writer, SkuBaseDto value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
