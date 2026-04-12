using System;

namespace EasyStock.Domain.ValueObjects
{
    public sealed record CodigoSku
    {
        public string Value { get; }

        private CodigoSku(string value)
        {
            Value = value;
        }

        public static CodigoSku From(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("SKU é obrigatório.", nameof(value));
            var normalized = value.Trim();
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
    }
}
