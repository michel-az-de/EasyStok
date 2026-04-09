using System;

namespace EasyStock.Domain.ValueObjects
{
    public sealed record Dinheiro
    {
        public decimal Valor { get; }

        private Dinheiro(decimal valor)
        {
            Valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
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

        public override string ToString() => Valor.ToString("F2");
    }
}
