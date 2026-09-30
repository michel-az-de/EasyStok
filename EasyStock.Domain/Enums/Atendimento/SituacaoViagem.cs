namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>Montando: aceita e retira paradas. EmRota: saiu. Concluida: todas entregues. Desfeita: dissolvida antes de sair.</summary>
public enum SituacaoViagem
{
    Montando = 1,
    EmRota = 2,
    Concluida = 3,
    Desfeita = 4
}
