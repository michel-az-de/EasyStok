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
    public string? FotosJson { get; init; }
    /// <summary>
    /// C2 (RDC 727/2022): "Avulso" (default) | "Embalado". Usado por Lotes
    /// para validar peso obrigatorio no front (campo opcional do API/serializa
    /// como string via HasConversion).
    /// </summary>
    public string? TipoEmbalagem { get; init; }

    public string StatusNome => Status == 0 ? "Ativo" : "Inativo";

    /// <summary>URL da primeira foto do produto, ou null se não tiver.</summary>
    public string? PrimeiraFotoUrl => FotoJsonHelper.PrimeiraUrl(FotosJson);
}

/// <summary>Utilitário para extrair dados de <c>FotosJson</c> sem duplicar lógica de parsing.</summary>
internal static class FotoJsonHelper
{
    /// <summary>Retorna a URL da primeira foto serializada em <paramref name="fotosJson"/>, ou null.</summary>
    public static string? PrimeiraUrl(string? fotosJson)
    {
        if (string.IsNullOrWhiteSpace(fotosJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(fotosJson);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return null;
            var first = arr[0];
            // Aceita tanto "url" (camelCase) quanto "Url" (PascalCase)
            if (first.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                return u.GetString();
            if (first.TryGetProperty("Url", out var u2) && u2.ValueKind == JsonValueKind.String)
                return u2.GetString();
            return null;
        }
        catch (JsonException) { return null; }
    }
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
