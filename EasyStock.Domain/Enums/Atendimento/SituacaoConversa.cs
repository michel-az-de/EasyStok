namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// Automatica: o agente responde. Assumida: a dona escreveu ou o agente escalou; o agente cala (RN-04).
/// Encerrada: terminal; a proxima mensagem do contato abre outra conversa.
/// O valor numerico e persistido e usado no indice parcial "uma conversa aberta por contato".
/// </summary>
public enum SituacaoConversa
{
    Automatica = 1,
    Assumida = 2,
    Encerrada = 3
}
