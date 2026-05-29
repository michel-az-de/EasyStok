using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    [JsonConverter(typeof(CodigoLoteJsonConverter))]
    public sealed record CodigoLote
    {
        public string Value { get; }

        private CodigoLote(string value)
        {
            Value = value;
        }

        public static CodigoLote From(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Código de lote é obrigatório.", nameof(value));

            var normalized = value.Trim();
            if (normalized.Length > 100) throw new ArgumentException("Codigo de lote muito longo.", nameof(value));

            foreach (var ch in normalized)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_' && ch != '/')
                    throw new ArgumentException("Codigo de lote contem caracteres invalidos.", nameof(value));
            }

            return new CodigoLote(normalized.ToUpperInvariant());
        }

        public override string ToString() => Value;

        private sealed class CodigoLoteJsonConverter : JsonConverter<CodigoLote>
        {
            public override CodigoLote? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                if (reader.TokenType == JsonTokenType.String)
                    return From(reader.GetString()!);

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    string? val = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName &&
                            reader.GetString()!.Equals("value", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            val = reader.GetString();
                        }
                    }
                    return val is null ? null : From(val);
                }

                throw new JsonException($"Unexpected token {reader.TokenType} for CodigoLote.");
            }

            public override void Write(Utf8JsonWriter writer, CodigoLote value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.Value);
        }
    }
}
