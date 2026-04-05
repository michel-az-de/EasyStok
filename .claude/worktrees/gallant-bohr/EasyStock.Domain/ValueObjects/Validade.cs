using System;

namespace EasyStock.Domain.ValueObjects
{
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

        public bool EstaVencido(DateTime? referencia = null)
        {
            var refDate = (referencia ?? DateTime.UtcNow).Date;
            return DataValidade < refDate;
        }

        public int DiasAteVencimento(DateTime? referencia = null)
        {
            var refDate = (referencia ?? DateTime.UtcNow).Date;
            return (DataValidade - refDate).Days;
        }

        public bool EstaProntoParaVencerEm(int dias, DateTime? referencia = null)
        {
            if (dias < 0) throw new ArgumentOutOfRangeException(nameof(dias), "Dias não pode ser negativo.");
            return DiasAteVencimento(referencia) <= dias;
        }

        public override string ToString() => DataValidade.ToString("yyyy-MM-dd");
    }
}
