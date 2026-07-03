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
        // StatusPedidoFornecedor (espelho literal de EasyStock.Domain/Enums/StatusPedidoFornecedor.cs,
        // 1-based — issue 797: o mapa anterior espelhava um enum que nao existe no Domain)
        ["Aberto"] = 1,
        ["EmTransito"] = 2,
        ["Recebido"] = 3,
        ["Cancelado"] = 4,
        ["RecebidoParcial"] = 5,
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
            // Nome fora do Map = espelhamento divergiu do Domain (classe da issue 797).
            // Trace em vez de throw: quebrar a deserializacao inteira seria pior que um
            // badge generico; o trace deixa a divergencia visivel em diagnostico.
            System.Diagnostics.Trace.TraceWarning(
                $"EnumStringOrIntConverter: nome de enum desconhecido '{str}' — espelho divergiu do Domain (issue 797).");
            return 0;
        }

        return 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
