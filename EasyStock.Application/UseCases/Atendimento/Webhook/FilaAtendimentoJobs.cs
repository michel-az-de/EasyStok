namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>Nomes das filas em processo (<see cref="Ports.Output.IQueueService"/>) do módulo de atendimento.</summary>
public static class FilaAtendimentoNomes
{
    public const string TurnoAgente = "atendimento:turno-agente";
    public const string MidiaWhatsApp = "atendimento:midia-whatsapp";
}

/// <summary>
/// Enfileirado pelo webhook (S03/S05) para o agente (S06) responder fora da requisição. Consumido por
/// <c>AtendimentoFilaTurnoAgenteBackgroundService</c> (Api), que chama <c>ProcessarTurnoAgenteUseCase</c>.
/// </summary>
public sealed record ProcessarTurnoAgenteJob(Guid EmpresaId, Guid ConversaId);

/// <summary>Enfileirado pelo webhook (S03) para <c>ArmazenadorMidiaWhatsApp</c> (S02) baixar a mídia fora da requisição.</summary>
public sealed record ArmazenarMidiaWhatsAppJob(Guid EmpresaId, Guid ConversaId, string Wamid, string MediaId);
