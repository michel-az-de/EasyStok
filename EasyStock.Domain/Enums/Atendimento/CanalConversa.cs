namespace EasyStock.Domain.Enums.Atendimento;

/// <summary>
/// Canal por onde a conversa chegou (ADR-0051: todos no go-live). O que cada canal suporta está em
/// <see cref="ValueObjects.CapacidadesCanal"/>.
/// </summary>
public enum CanalConversa
{
    WhatsApp = 1,
    Instagram = 2,
    Messenger = 3,
    ChatSite = 4,
    Email = 5,
    Sms = 6
}
