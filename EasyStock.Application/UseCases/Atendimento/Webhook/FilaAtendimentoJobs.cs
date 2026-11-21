namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>Nomes das filas em processo (<see cref="Ports.Output.IQueueService"/>) do módulo de atendimento.</summary>
public static class FilaAtendimentoNomes
{
    public const string TurnoAgente = "atendimento:turno-agente";
    public const string MidiaWhatsApp = "atendimento:midia-whatsapp";
    public const string FotoPerfilWhatsApp = "atendimento:foto-perfil-whatsapp";
}

/// <summary>
/// Enfileirado pelo webhook (S03/S05) para o agente (S06) responder fora da requisição. Consumido por
/// <c>AtendimentoFilaTurnoAgenteBackgroundService</c> (Api), que chama <c>ProcessarTurnoAgenteUseCase</c>.
/// <see cref="Retomada"/> (#3068): turno da varredura de conversa parada com a equipe; o turno decide se retoma.
/// </summary>
public sealed record ProcessarTurnoAgenteJob(Guid EmpresaId, Guid ConversaId, bool Retomada = false);

/// <summary>Enfileirado pelo webhook (S03) para <c>ArmazenadorMidiaWhatsApp</c> (S02) baixar a mídia fora da requisição.</summary>
public sealed record ArmazenarMidiaWhatsAppJob(Guid EmpresaId, Guid ConversaId, string Wamid, string MediaId);

/// <summary>
/// #2155: enfileirado pelo webhook na conversa nova de cliente sem foto, para <c>ColetarFotoPerfilWhatsAppUseCase</c>
/// buscar a foto do perfil fora da requisição. <see cref="Contato"/> é o contato da conversa (wa_id).
/// </summary>
public sealed record ColetarFotoPerfilWhatsAppJob(Guid EmpresaId, Guid ClienteId, string Contato);
