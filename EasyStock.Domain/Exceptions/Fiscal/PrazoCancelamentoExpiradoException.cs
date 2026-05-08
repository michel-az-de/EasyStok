namespace EasyStock.Domain.Exceptions.Fiscal;

/// <summary>
/// Lançada quando o operador tenta cancelar uma NFC-e após o prazo legal
/// de 30 minutos contados a partir da autorização. SEFAZ não aceita
/// cancelamentos após esse prazo — o erro evita uma chamada externa que
/// já se sabe que falhará.
/// </summary>
public sealed class PrazoCancelamentoExpiradoException : Exception
{
    public DateTime DataAutorizacao { get; }
    public double MinutosDecorridos { get; }

    public PrazoCancelamentoExpiradoException(DateTime dataAutorizacao, double minutosDecorridos)
        : base($"Prazo de cancelamento de 30 minutos excedido. Autorização: {dataAutorizacao:O}. Decorrido: {minutosDecorridos:F1} min.")
    {
        DataAutorizacao = dataAutorizacao;
        MinutosDecorridos = minutosDecorridos;
    }
}
