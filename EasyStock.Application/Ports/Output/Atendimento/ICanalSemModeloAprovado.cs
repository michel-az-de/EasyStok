namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// O adaptador deste canal não envia modelo aprovado (ponte não oficial do WhatsApp, #2072, ADR-0062). Onde a Meta
/// mandaria o modelo, quem chama manda o texto da própria mensagem. A janela de atendimento continua valendo: a ponte
/// envia o que a Meta enviaria, nada a mais.
/// </summary>
public interface ICanalSemModeloAprovado
{
}
