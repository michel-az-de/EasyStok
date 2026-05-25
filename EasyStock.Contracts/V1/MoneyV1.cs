using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Contracts.V1;

/// <summary>
/// Representação monetária no contrato V1. JSON serializa como
/// <c>number</c> simples (decimal), preservando shape esperado por
/// PWA, mobile e MAUI (<c>"total": 50.00</c>).
///
/// <para>
/// Currency é fixado em "BRL" — multi-moeda fica fora do escopo desta
/// versão (ver não-objetivos no plano).
/// </para>
/// </summary>
[JsonConverter(typeof(MoneyV1JsonConverter))]
public readonly record struct MoneyV1(decimal Amount)
{
    public string Currency => "BRL";

    public static MoneyV1 Zero => new(0m);
    public static MoneyV1 FromDecimal(decimal amount) => new(amount);

    public static implicit operator decimal(MoneyV1 m) => m.Amount;

    public override string ToString() => Amount.ToString("F2");
}

internal sealed class MoneyV1JsonConverter : JsonConverter<MoneyV1>
{
    public override MoneyV1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return new MoneyV1(reader.GetDecimal());

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            decimal? amount = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType == JsonTokenType.PropertyName &&
                    reader.GetString()!.Equals("amount", StringComparison.OrdinalIgnoreCase))
                {
                    reader.Read();
                    amount = reader.GetDecimal();
                }
            }
            return amount.HasValue ? new MoneyV1(amount.Value) : MoneyV1.Zero;
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for MoneyV1.");
    }

    public override void Write(Utf8JsonWriter writer, MoneyV1 value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Amount);
}
