namespace EasyStock.Domain.Enums.Campanhas;

/// <summary>
/// Ciclo da campanha (S28): Rascunho → Agendada → Enviando (onda em curso) → Enviada (onda
/// concluída, S30) → Encerrada. Cancelada a qualquer momento antes de Encerrada.
/// </summary>
public enum StatusCampanha
{
    Rascunho = 1,
    Agendada = 2,
    Enviando = 3,
    Enviada = 4,
    Encerrada = 5,
    Cancelada = 6
}
