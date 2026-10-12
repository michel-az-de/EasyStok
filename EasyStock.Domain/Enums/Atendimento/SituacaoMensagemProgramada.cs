namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// Ciclo da mensagem programada (S39): Agendada → Enviando (reservada por um disparador) → Enviada
/// ou Falhou. Só Agendada pode ser Cancelada.
/// </summary>
public enum SituacaoMensagemProgramada
{
    Agendada = 1,
    Enviando = 2,
    Enviada = 3,
    Cancelada = 4,
    Falhou = 5
}
