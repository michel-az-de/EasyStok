namespace EasyStock.Domain.Enums.Notifications;

/// <summary>
/// De quem a mensagem sai (N6, ADR-0057). É propriedade do tipo do evento (<c>RemetentePorTipoEvento</c>), nunca de
/// tela: nenhum Admin troca.
/// </summary>
public enum OrigemRemetente
{
    /// <summary>A loja: número da empresa do tenant, com histórico na <c>Conversa</c> (aviso e campanha ao cliente final).</summary>
    Loja = 1,

    /// <summary>A plataforma: 2º número da WABA, só template, sem tocar a <c>Conversa</c> (sistema para usuário interno).</summary>
    Plataforma = 2
}
