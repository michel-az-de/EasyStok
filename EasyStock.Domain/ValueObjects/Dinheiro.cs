using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    [JsonConverter(typeof(DinheiroJsonConverter))]
    public sealed record Dinheiro
    {
        public decimal Valor { get; }

        private Dinheiro(decimal valor)
        {
            Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        }

        public static Dinheiro FromDecimal(decimal valor)
        {
            if (valor < 0) throw new ArgumentOutOfRangeException(nameof(valor), "Valor monetário não pode ser negativo.");
            return new Dinheiro(valor);
        }

        public static Dinheiro Zero => new(0m);

        public Dinheiro Add(Dinheiro other) => FromDecimal(Valor + other.Valor);
        public Dinheiro Subtract(Dinheiro other)
        {
            var result = Valor - other.Valor;
            if (result < 0) throw new InvalidOperationException("Operação resultaria em valor monetário negativo.");
            return FromDecimal(result);
        }

        public static implicit operator decimal(Dinheiro d) => d.Valor;
        public static implicit operator decimal?(Dinheiro? d) => d?.Valor;

        public override string ToString() => Valor.ToString("F2");

        private sealed class DinheiroJsonConverter : JsonConverter<Dinheiro>
        {
            public override Dinheiro? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                if (reader.TokenType == JsonTokenType.Number)
                    return FromDecimal(reader.GetDecimal());

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    decimal? val = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName &&
                            reader.GetString()!.Equals("valor", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            val = reader.GetDecimal();
                        }
                    }
                    return val.HasValue ? FromDecimal(val.Value) : null;
                }

                throw new JsonException($"Unexpected token {reader.TokenType} for Dinheiro.");
            }

            public override void Write(Utf8JsonWriter writer, Dinheiro value, JsonSerializerOptions options)
                => writer.WriteNumberValue(value.Valor);
        }
    }
}
