using System;

namespace EasyStock.Domain.ValueObjects
{
    public sealed record Dimensoes
    {
        public decimal Peso { get; }
        public decimal Largura { get; }
        public decimal Altura { get; }
        public decimal Comprimento { get; }

        private Dimensoes(decimal peso, decimal largura, decimal altura, decimal comprimento)
        {
            Peso = Math.Round(peso, 3, MidpointRounding.AwayFromZero);
            Largura = Math.Round(largura, 2, MidpointRounding.AwayFromZero);
            Altura = Math.Round(altura, 2, MidpointRounding.AwayFromZero);
            Comprimento = Math.Round(comprimento, 2, MidpointRounding.AwayFromZero);
        }

        public static Dimensoes From(decimal peso, decimal largura, decimal altura, decimal comprimento)
        {
            if (peso < 0) throw new ArgumentOutOfRangeException(nameof(peso), "Peso nao pode ser negativa.");
            if (largura < 0) throw new ArgumentOutOfRangeException(nameof(largura), "Largura nao pode ser negativa.");
            if (altura < 0) throw new ArgumentOutOfRangeException(nameof(altura), "Altura nao pode ser negativa.");
            if (comprimento < 0) throw new ArgumentOutOfRangeException(nameof(comprimento), "Comprimento nao pode ser negativa.");

            return new Dimensoes(peso, largura, altura, comprimento);
        }

        public bool EstaVazio() =>
            Peso == 0m &&
            Largura == 0m &&
            Altura == 0m &&
            Comprimento == 0m;

        public override string ToString() =>
            $"P:{Peso:F3} L:{Largura:F2} A:{Altura:F2} C:{Comprimento:F2}";
    }
}
