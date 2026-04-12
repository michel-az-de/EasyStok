using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    [JsonConverter(typeof(DimensoesJsonConverter))]
    public sealed record Dimensoes
    {
        public decimal Peso { get; }
        public decimal Largura { get; }
        public decimal Altura { get; }
        public decimal Comprimento { get; }

        private Dimensoes(decimal peso, decimal largura, decimal altura, decimal comprimento)
        {
            Peso = Math.Round(peso, 3, MidpointRounding.AwayFromZero);
            Largura = Math.Round(largura, 2, MidpointRounding.AwayFromZero);
            Altura = Math.Round(altura, 2, MidpointRounding.AwayFromZero);
            Comprimento = Math.Round(comprimento, 2, MidpointRounding.AwayFromZero);
        }

        public static Dimensoes From(decimal peso, decimal largura, decimal altura, decimal comprimento)
        {
            if (peso < 0) throw new ArgumentOutOfRangeException(nameof(peso), "Peso nao pode ser negativa.");
            if (largura < 0) throw new ArgumentOutOfRangeException(nameof(largura), "Largura nao pode ser negativa.");
            if (altura < 0) throw new ArgumentOutOfRangeException(nameof(altura), "Altura nao pode ser negativa.");
            if (comprimento < 0) throw new ArgumentOutOfRangeException(nameof(comprimento), "Comprimento nao pode ser negativa.");

            return new Dimensoes(peso, largura, altura, comprimento);
        }

        public bool EstaVazio() =>
            Peso == 0m &&
            Largura == 0m &&
            Altura == 0m &&
            Comprimento == 0m;

        public override string ToString() =>
            $"P:{Peso:F3} L:{Largura:F2} A:{Altura:F2} C:{Comprimento:F2}";

        private sealed class DimensoesJsonConverter : JsonConverter<Dimensoes>
        {
            public override Dimensoes? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                if (reader.TokenType != JsonTokenType.StartObject)
                    throw new JsonException($"Unexpected token {reader.TokenType} for Dimensoes.");

                decimal peso = 0, largura = 0, altura = 0, comprimento = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;
                    var prop = reader.GetString()!;
                    reader.Read();

                    if (prop.Equals("peso", StringComparison.OrdinalIgnoreCase)) peso = reader.GetDecimal();
                    else if (prop.Equals("largura", StringComparison.OrdinalIgnoreCase)) largura = reader.GetDecimal();
                    else if (prop.Equals("altura", StringComparison.OrdinalIgnoreCase)) altura = reader.GetDecimal();
                    else if (prop.Equals("comprimento", StringComparison.OrdinalIgnoreCase)) comprimento = reader.GetDecimal();
                }

                return From(peso, largura, altura, comprimento);
            }

            public override void Write(Utf8JsonWriter writer, Dimensoes value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteNumber("peso", value.Peso);
                writer.WriteNumber("largura", value.Largura);
                writer.WriteNumber("altura", value.Altura);
                writer.WriteNumber("comprimento", value.Comprimento);
                writer.WriteEndObject();
            }
        }
    }
}
