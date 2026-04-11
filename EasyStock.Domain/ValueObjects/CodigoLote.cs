using System;

namespace EasyStock.Domain.ValueObjects
{
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
    }
}
