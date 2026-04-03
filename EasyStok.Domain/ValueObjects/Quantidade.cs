using System;

namespace EasyStok.Domain.ValueObjects
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

        public static Quantidade Zero => new Quantidade(0);
        public Quantidade Add(Quantidade other) => From(Value + other.Value);
        public Quantidade Subtract(Quantidade other)
        {
            var result = Value - other.Value;
            if (result < 0) throw new InvalidOperationException("Resultado da subtração resultaria em quantidade negativa.");
            return From(result);
        }

        public override string ToString() => Value.ToString();
    }
}
