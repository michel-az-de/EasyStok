namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>Nomes das filas em processo (<see cref="Ports.Output.IQueueService"/>) do módulo de atendimento.</summary>
public static class FilaAtendimentoNomes
{
    public const string TurnoAgente = "atendimento:turno-agente";
    public const string MidiaWhatsApp = "atendimento:midia-whatsapp";
}

/// <summary>
/// Enfileirado pelo webhook (S03) para o agente (S06) responder fora da requisição. S06 ainda não
/// existe: por ora nada drena esta fila além dos testes, que chamam <see cref="Ports.Output.IQueueService.ProcessQueueAsync{T}"/>
/// diretamente para provar que o job enfileirado é processável.
/// </summary>
public sealed record ProcessarTurnoAgenteJob(Guid EmpresaId, Guid ConversaId);

/// <summary>Enfileirado pelo webhook (S03) para <c>ArmazenadorMidiaWhatsApp</c> (S02) baixar a mídia fora da requisição.</summary>
public sealed record ArmazenarMidiaWhatsAppJob(Guid EmpresaId, Guid ConversaId, string Wamid, string MediaId);
