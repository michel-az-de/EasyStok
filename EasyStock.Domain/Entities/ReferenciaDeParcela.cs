namespace EasyStock.Domain.Entities;

/// <summary>
/// Referência do <see cref="MovimentoCaixa"/> que nasce de uma parcela (contas a pagar e a receber). #1769: a referência
/// era montada com os três identificadores por extenso e dava 125 caracteres, 5 a mais que a coluna (120). Todo
/// pagamento de parcela respondia erro interno e nada era gravado. Os identificadores vão no formato compacto (32).
/// </summary>
public static class ReferenciaDeParcela
{
    /// <summary>Limite da coluna <c>movimentos_caixa.Referencia</c>.</summary>
    public const int TamanhoMaximo = 120;

    public static string DoPagamento(string origem, Guid contaId, Guid parcelaId, Guid pagamentoId) =>
        Montar(origem, contaId, parcelaId, "pag", pagamentoId.ToString("N"));

    /// <summary>O txid do Pix tem até 35 caracteres; com eles a referência fica em 117.</summary>
    public static string DoPix(string origem, Guid contaId, Guid parcelaId, string txid) =>
        Montar(origem, contaId, parcelaId, "txid", txid);

    private static string Montar(string origem, Guid contaId, Guid parcelaId, string tipo, string valor)
    {
        var referencia = $"{origem}:{contaId:N}:parcela:{parcelaId:N}:{tipo}:{valor}";
        // Nunca deixa a gravação inteira falhar por causa de um rótulo: o que passar do limite é cortado no fim.
        return referencia.Length <= TamanhoMaximo ? referencia : referencia[..TamanhoMaximo];
    }
}
