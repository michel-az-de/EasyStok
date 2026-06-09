using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    [JsonConverter(typeof(ValidadeJsonConverter))]
    public sealed record Validade
    {
        public DateTime DataValidade { get; }

        private Validade(DateTime dataValidade)
        {
            // Normalize to date only (sem tempo) para comparações domain-friendly
            DataValidade = dataValidade.Date;
        }

        public static Validade From(DateTime dataValidade)
        {
            // Considerar validade no passado ainda é válido para representar um lote vencido; permitir.
            return new Validade(dataValidade);
        }

        /// <summary>
        /// True se a validade ja passou (DataValidade &lt; referencia). Requer
        /// a data civil de Brasilia — nunca passe UtcNow.Date diretamente; use
        /// HorarioBrasil.Hoje() (Application) ou OperacionalFuso.DataOperacional()
        /// (Domain) para obter o dia operacional correto.
        /// </summary>
        public bool EstaVencido(DateOnly referencia)
            => DateOnly.FromDateTime(DataValidade) < referencia;

        /// <summary>
        /// Dias restantes ate o vencimento (negativo = ja vencido). Requer
        /// a data civil de Brasilia — mesma observacao de <see cref="EstaVencido"/>.
        /// </summary>
        public int DiasAteVencimento(DateOnly referencia)
            => DateOnly.FromDateTime(DataValidade).DayNumber - referencia.DayNumber;

        /// <summary>True se o produto vence em ate <paramref name="dias"/> dias.</summary>
        public bool EstaProntoParaVencerEm(int dias, DateOnly referencia)
        {
            if (dias < 0) throw new ArgumentOutOfRangeException(nameof(dias), "Dias não pode ser negativo.");
            return DiasAteVencimento(referencia) <= dias;
        }

        public static implicit operator DateTime(Validade v) => v.DataValidade;
        public static implicit operator DateTime?(Validade? v) => v?.DataValidade;

        public override string ToString() => DataValidade.ToString("yyyy-MM-dd");

        private sealed class ValidadeJsonConverter : JsonConverter<Validade>
        {
            public override Validade? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null) return null;
                if (reader.TokenType == JsonTokenType.String)
                {
                    var str = reader.GetString()!;
                    return From(DateTime.Parse(str));
                }

                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    DateTime? val = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType == JsonTokenType.PropertyName &&
                            reader.GetString()!.Equals("dataValidade", StringComparison.OrdinalIgnoreCase))
                        {
                            reader.Read();
                            val = reader.GetDateTime();
                        }
                    }
                    return val.HasValue ? From(val.Value) : null;
                }

                throw new JsonException($"Unexpected token {reader.TokenType} for Validade.");
            }

            public override void Write(Utf8JsonWriter writer, Validade value, JsonSerializerOptions options)
                => writer.WriteStringValue(value.DataValidade.ToString("yyyy-MM-dd"));
        }
    }
}
