namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// Ciclo de entrega de uma mensagem de saida na Meta (sent, delivered, read, failed).
/// A ordem numerica importa: o status so avanca (Pendente &lt; Enviada &lt; Entregue &lt; Lida); Falhou e terminal.
/// Mensagem de entrada nasce Entregue.
/// </summary>
public enum StatusMensagem
{
    Pendente = 1,
    Enviada = 2,
    Entregue = 3,
    Lida = 4,
    Falhou = 5
}
