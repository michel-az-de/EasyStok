namespace EasyStock.Domain.Enums.Notifications;

/// <summary>
/// Estado da mensagem no outbox. Vai ao banco como texto (<c>varchar(20)</c>, sem CHECK nem migration por
/// valor novo): nunca repetir valor nem renomear membro publicado, porque o EF lê o nome (ADR-0057).
/// Terminais: tudo fora de <see cref="Pendente"/> e <see cref="EmEnvio"/>.
/// </summary>
public enum StatusOutbox
{
    Pendente = 1,
    EmEnvio = 2,
    Enviado = 3,
    Falhado = 4,
    Cancelado = 5,
    Suprimido = 6,

    /// <summary>
    /// Stub ou console: o provider não envia nada e disse isso (N2). Terminal, sem <c>EnviadoEm</c>; para a
    /// campanha conta como falha, porque nada saiu.
    /// </summary>
    Simulado = 7,

    /// <summary>
    /// Não há como saber se o provider entregou (timeout, queda de conexão ou 5xx no WhatsApp e no SMS, que
    /// saem no máximo uma vez). Terminal: nunca é reenviado nem cai no fallback de canal (N2).
    /// </summary>
    Indeterminado = 8,

    /// <summary>
    /// Passou do prazo de validade do tipo antes de sair (N1, quarentena): backlog velho não se envia. Terminal, sem
    /// <c>EnviadoEm</c>; para a campanha conta como falha, porque nada saiu.
    /// </summary>
    Expirado = 9
}
