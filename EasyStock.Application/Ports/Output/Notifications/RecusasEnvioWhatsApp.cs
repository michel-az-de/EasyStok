namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Recusas que o provider de WhatsApp do outbox devolve em <see cref="ResultadoEnvio.ErroDetalhado"/> antes de chamar a
/// Meta. Ficam aqui para quem lê o resultado (#2875: o aviso não entregue na conversa) não depender do texto solto.
/// </summary>
public static class RecusasEnvioWhatsApp
{
    /// <summary>Cliente bloqueado na loja ou que não é dela.</summary>
    public const string ClienteBloqueado = "cliente_bloqueado_ou_indisponivel";

    /// <summary>Cliente revogou o contato transacional pelo WhatsApp.</summary>
    public const string TransacionalRevogado = "contato_transacional_revogado";

    /// <summary>Cliente pediu SAIR do marketing depois de a onda ser enfileirada (#2746).</summary>
    public const string MarketingRevogado = "contato_marketing_revogado";

    /// <summary>Texto livre fora da janela de 24 h, sem modelo aprovado para mandar no lugar.</summary>
    public const string ForaDaJanelaSemModelo = "fora_da_janela_24h_sem_template";

    /// <summary>O cliente pediu para não receber: a recusa não é falha a reenviar.</summary>
    public static bool PorConsentimento(string? erro) =>
        erro is ClienteBloqueado or TransacionalRevogado or MarketingRevogado;
}
