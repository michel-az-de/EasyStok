using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Domain.ValueObjects
{
    [JsonConverter(typeof(DinheiroJsonConverter))]
    public sealed record Dinheiro
    {
        public decimal Valor { get; }

        /// <summary>Regra única de arredondamento monetário do domínio (#2373): 2 casas, meio para longe do zero.</summary>
        public const MidpointRounding Arredondamento = MidpointRounding.AwayFromZero;

        public static decimal Arredondar(decimal valor) => Math.Round(valor, 2, Arredondamento);

        private Dinheiro(decimal valor)
        {
            Valor = Arredondar(valor);
        }

        /// <summary>
        /// Rateia <paramref name="precoUnitario"/> × quantidade entre os lotes sem perder centavo (#2373): cada lote
        /// leva o próprio valor arredondado, limitado ao saldo, e o último leva o resíduo.
        /// </summary>
        public static IReadOnlyList<Dinheiro> Ratear(decimal precoUnitario, IReadOnlyList<decimal> quantidades)
        {
            if (quantidades.Count == 0) return Array.Empty<Dinheiro>();

            var total = Arredondar(precoUnitario * quantidades.Sum());
            var partes = new Dinheiro[quantidades.Count];
            var acumulado = 0m;
            for (var i = 0; i < quantidades.Count - 1; i++)
            {
                var valor = FromDecimal(precoUnitario * quantidades[i]);
                partes[i] = FromDecimal(Math.Min(valor.Valor, total - acumulado));
                acumulado += partes[i].Valor;
            }
            partes[^1] = FromDecimal(Math.Max(0m, total - acumulado));
            return partes;
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

        public override string ToString() => Valor.ToString("F2", CultureInfo.InvariantCulture);

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
