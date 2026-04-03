using System;

namespace EasyStok.Domain.ValueObjects
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
            // Regras simples: permitir letras, dígitos, '-' e '_' com comprimento razoável
            if (normalized.Length > 100) throw new ArgumentException("SKU muito longo.", nameof(value));
            foreach (var ch in normalized)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_')
                    throw new ArgumentException("SKU contém caracteres inválidos. Apenas letras, dígitos, '-' e '_' são permitidos.", nameof(value));
            }
            return new CodigoSku(normalized.ToUpperInvariant());
        }

        public override string ToString() => Value;
    }
}
