using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    [JsonConverter(typeof(QuantidadeJsonConverter))]
    public sealed record Quantidade
    {
        public int Value { get; }

        private Quantidade(int value)
        {
            Value = value;
        }

        public static Quantidade From(int value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Quantidade não pode ser negativa.");
            return new Quantidade(value);
        }

        public static Quantidade Zero => new(0);
        public Quantidade Add(Quantidade other) => From(Value + other.Value);
        public Quantidade Subtract(Quantidade other)
        {
            var result = Value - other.Value;
            if (result < 0) throw new InvalidOperationException("Resultado da subtração resultaria em quantidade negativa.");
            return From(result);
        }

        public static implicit operator int(Quantidade q) => q.Value;
        public static implicit operator int?(Quantidade? q) => q?.Value;

        public override string ToString() => Value.ToString();

        private sealed class QuantidadeJsonConverter : JsonConverter<Quantidade>
        {
            public override Quantidade? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                if (reader.TokenType == JsonTokenType.Number)
                    return From(reader.GetInt32());

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    int? val = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName &&
                            reader.GetString()!.Equals("value", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            val = reader.GetInt32();
                        }
                    }
                    return val.HasValue ? From(val.Value) : null;
                }

                throw new JsonException($"Unexpected token {reader.TokenType} for Quantidade.");
            }

            public override void Write(Utf8JsonWriter writer, Quantidade value, JsonSerializerOptions options)
                => writer.WriteNumberValue(value.Value);
        }
    }
}
