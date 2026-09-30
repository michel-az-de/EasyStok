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
    Encerramento = 6
}
