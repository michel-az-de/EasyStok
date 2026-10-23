namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// Gatilho fechado das mensagens automáticas (S42, protótipo <c>dominio/automacao.js</c>). Os avisos de
/// status da esteira são do S13 e não entram aqui.
/// </summary>
public enum GatilhoAutomacao
{
    PrimeiroContato = 1,
    ForaDoHorario = 2,
    LojaFechada = 3,
    PagamentoConfirmado = 4,
    PosEntrega = 5,
    Encerramento = 6,

    /// <summary>
    /// #1610: link de pagamento pendente da conversa perto de vencer. Só avisa: não reemite nem cancela. Único gatilho
    /// que nasce ligado (<see cref="Entities.Atendimento.RegraAutomatica.Padrao"/>).
    /// </summary>
    CobrancaVencendo = 7
}
