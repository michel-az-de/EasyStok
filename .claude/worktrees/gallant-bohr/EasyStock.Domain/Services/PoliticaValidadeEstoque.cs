using System;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Domain.Services
{
    public sealed class PoliticaValidadeEstoque
    {
        // Decide ações operacionais simples baseadas na validade
        public enum AcaoValidade
        {
            Nenhuma,
            Descartar,
            SepararParaQuarentena,
            PriorizarVenda
        }

        public AcaoValidade Avaliar(Validade validade, DateTime? referencia = null, int diasPriorizarVenda = 7)
        {
            if (diasPriorizarVenda < 0) throw new ArgumentOutOfRangeException(nameof(diasPriorizarVenda));
            var refDate = (referencia ?? DateTime.UtcNow).Date;
            var dias = validade.DiasAteVencimento(refDate);
            if (dias < 0) return AcaoValidade.Descartar;
            if (dias == 0) return AcaoValidade.SepararParaQuarentena;
            if (dias <= diasPriorizarVenda) return AcaoValidade.PriorizarVenda;
            return AcaoValidade.Nenhuma;
        }
    }
}
