

namespace EasyStok.Domain.ValueObjects
{
    public sealed record Dimensoes
    {
        public decimal PesoKg { get; }
        public decimal LarguraCm { get; }
        public decimal AlturaCm { get; }
        public decimal ComprimentoCm { get; }

        private Dimensoes(decimal pesoKg, decimal larguraCm, decimal alturaCm, decimal comprimentoCm)
        {
            PesoKg = pesoKg;
            LarguraCm = larguraCm;
            AlturaCm = alturaCm;
            ComprimentoCm = comprimentoCm;
        }

        public static Dimensoes From(decimal pesoKg, decimal larguraCm, decimal alturaCm, decimal comprimentoCm)
        {

            if (pesoKg < 0) throw new ArgumentOutOfRangeException(nameof(pesoKg), "Peso não pode ser negativo.");
            if (larguraCm < 0) throw new ArgumentOutOfRangeException(nameof(larguraCm), "Largura não pode ser negativa.");
            if (alturaCm < 0) throw new ArgumentOutOfRangeException(nameof(alturaCm), "Altura não pode ser negativa.");
            if (comprimentoCm < 0) throw new ArgumentOutOfRangeException(nameof(comprimentoCm), "Comprimento não pode ser negativo.");

            return new Dimensoes(Math.Round(pesoKg, 3), Math.Round(larguraCm, 2), Math.Round(alturaCm, 2), Math.Round(comprimentoCm, 2));
        }

        public decimal VolumeCm3() => LarguraCm * AlturaCm * ComprimentoCm;

        public override string ToString() => $"Peso: {PesoKg:F3} kg, {LarguraCm:F2}x{AlturaCm:F2}x{ComprimentoCm:F2} cm";
    }
}
