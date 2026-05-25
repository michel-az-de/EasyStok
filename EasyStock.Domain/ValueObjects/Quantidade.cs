using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    [JsonConverter(typeof(QuantidadeJsonConverter))]
    public sealed record Quantidade
    {
        public decimal Value { get; }

        private Quantidade(decimal value)
        {
            Value = value;
        }

        public static Quantidade From(decimal value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Quantidade não pode ser negativa.");
            return new Quantidade(value);
        }

        // Overload mantido para compatibilidade com call-sites que passam int literal
        public static Quantidade From(int value) => From((decimal)value);

        public static Quantidade Zero => new(0m);
        public Quantidade Add(Quantidade other) => From(Value + other.Value);
        public Quantidade Subtract(Quantidade other)
        {
            var result = Value - other.Value;
            if (result < 0) throw new InvalidOperationException("Resultado da subtração resultaria em quantidade negativa.");
            return From(result);
        }

        // Operadores decimais (primários)
        public static implicit operator decimal(Quantidade q) => q.Value;
        public static implicit operator decimal?(Quantidade? q) => q?.Value;

        // Operadores int mantidos para retrocompatibilidade com sites que consomem int
        public static implicit operator int(Quantidade q) => (int)q.Value;
        public static implicit operator int?(Quantidade? q) => q == null ? (int?)null : (int)q.Value;

        public override string ToString() => Value.ToString("G");

        private sealed class QuantidadeJsonConverter : JsonConverter<Quantidade>
        {
            public override Quantidade? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                if (reader.TokenType == JsonTokenType.Number)
                    return From(reader.GetDecimal());

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    decimal? val = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName &&
                            reader.GetString()!.Equals("value", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            val = reader.GetDecimal();
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
