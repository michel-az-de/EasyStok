using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Web.Models.Api;

/// <summary>
/// Converter that reads both string enum names ("Ativo", "Fisico") and int values (0, 1)
/// from the API, which uses JsonStringEnumConverter and serializes enums as strings.
/// Maps known enum names to their ordinal int values.
/// </summary>
internal sealed class EnumStringOrIntConverter : JsonConverter<int>
{
    private static readonly Dictionary<string, int> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // StatusProduto
        ["Ativo"] = 0,
        ["Inativo"] = 1,
        // TipoProduto
        ["Fisico"] = 0,
        ["Alimento"] = 1,
        ["Servico"] = 2,
        // NaturezaMovimentacao
        ["Entrada"] = 0,
        ["Saida"] = 1,
        // StatusPedidoCompra
        ["Rascunho"] = 0,
        ["Enviado"] = 1,
        ["Confirmado"] = 2,
        ["Entregue"] = 3,
        ["Cancelado"] = 4,
    };

    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetInt32();

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString()!;
            if (int.TryParse(str, out var n)) return n;
            if (Map.TryGetValue(str, out var mapped)) return mapped;
            return 0;
        }

        return 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
