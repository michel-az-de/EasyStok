namespace EasyStock.Domain.Enums
{
    /// <summary>
    /// Motivo de perda na produção (M2.6, #1511, D-M2-02 = a). O motivo É a natureza da saída, sem
    /// campo novo: o relatório agrupa pela natureza. "Outro" exige texto.
    /// </summary>
    public enum MotivoPerda
    {
        Vencido = 1,
        PerdaNoPreparo = 2,
        Doacao = 3,
        Degustacao = 4,
        Outro = 5
    }

    public static class MotivoPerdaExtensions
    {
        public static NaturezaMovimentacaoEstoque Natureza(this MotivoPerda motivo) => motivo switch
        {
            MotivoPerda.Vencido => NaturezaMovimentacaoEstoque.Vencimento,
            MotivoPerda.PerdaNoPreparo => NaturezaMovimentacaoEstoque.Perda,
            MotivoPerda.Doacao => NaturezaMovimentacaoEstoque.Doacao,
            MotivoPerda.Degustacao => NaturezaMovimentacaoEstoque.UsoInterno,
            MotivoPerda.Outro => NaturezaMovimentacaoEstoque.Prejuizo,
            _ => throw new ArgumentOutOfRangeException(nameof(motivo), motivo, "Motivo de perda desconhecido."),
        };

        /// <summary>O caminho de volta, para o relatório. Null = a natureza não é perda (Venda, Ajuste...).</summary>
        public static MotivoPerda? MotivoDaPerda(this NaturezaMovimentacaoEstoque natureza) => natureza switch
        {
            NaturezaMovimentacaoEstoque.Vencimento => MotivoPerda.Vencido,
            NaturezaMovimentacaoEstoque.Perda => MotivoPerda.PerdaNoPreparo,
            NaturezaMovimentacaoEstoque.Doacao => MotivoPerda.Doacao,
            NaturezaMovimentacaoEstoque.UsoInterno => MotivoPerda.Degustacao,
            NaturezaMovimentacaoEstoque.Prejuizo => MotivoPerda.Outro,
            _ => null,
        };

        public static string Rotulo(this MotivoPerda motivo) => motivo switch
        {
            MotivoPerda.Vencido => "Vencido",
            MotivoPerda.PerdaNoPreparo => "Perda no preparo",
            MotivoPerda.Doacao => "Doação",
            MotivoPerda.Degustacao => "Degustação",
            _ => "Outro",
        };
    }
}
