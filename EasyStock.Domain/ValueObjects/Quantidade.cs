using System;

namespace EasyStock.Domain.ValueObjects
{
    public sealed record Quantidade
    {
        public int Value { get; }

        private Quantidade(int value)
        {
            Value = value;
        }

        public static Quantidade From(int value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Quantidade não pode ser negativa.");
            return new Quantidade(value);
        }

        public static Quantidade Zero => new(0);
        public Quantidade Add(Quantidade other) => From(Value + other.Value);
        public Quantidade Subtract(Quantidade other)
        {
            var result = Value - other.Value;
            if (result < 0) throw new InvalidOperationException("Resultado da subtração resultaria em quantidade negativa.");
            return From(result);
        }

        public static implicit operator int(Quantidade q) => q.Value;
        public static implicit operator int?(Quantidade? q) => q?.Value;

        public override string ToString() => Value.ToString();
    }
}
