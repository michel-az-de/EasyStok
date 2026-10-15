namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>Quem abriu a ocorrência (S27): avaliação negativa, agente que detectou reclamação ou a dona.</summary>
public enum OrigemOcorrencia
{
    Avaliacao = 0,
    Agente = 1,
    Dona = 2,
}
