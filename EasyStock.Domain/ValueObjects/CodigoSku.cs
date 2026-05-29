using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    [JsonConverter(typeof(CodigoSkuJsonConverter))]
    public sealed record CodigoSku
    {
        public string Value { get; }

        private CodigoSku(string value)
        {
            Value = value;
        }

        public static CodigoSku From(string value)
        {
            var normalized = value?.Trim();
            if (string.IsNullOrEmpty(normalized)) throw new ArgumentException("SKU é obrigatório.", nameof(value));
            if (normalized.Length > 100) throw new ArgumentException("SKU muito longo.", nameof(value));
            foreach (var ch in normalized)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_')
                    throw new ArgumentException("SKU contém caracteres inválidos. Apenas letras, dígitos, '-' e '_' são permitidos.", nameof(value));
            }
            return new CodigoSku(normalized.ToUpperInvariant());
        }

        public static implicit operator string?(CodigoSku? s) => s?.Value;

        public override string ToString() => Value;

        private sealed class CodigoSkuJsonConverter : JsonConverter<CodigoSku>
        {
            public override CodigoSku? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                if (reader.TokenType == JsonTokenType.String)
                    return From(reader.GetString()!);

                // Backward compat: { "Value": "..." }
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

                throw new JsonException($"Unexpected token {reader.TokenType} for CodigoSku.");
            }

            public override void Write(Utf8JsonWriter writer, CodigoSku value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.Value);
        }
    }
}
