namespace EasyStock.Domain.Enums.Notifications;

public enum StatusEventoNotificacao
{
    Pendente = 1,
    Processado = 2,
    Falhado = 3,

    /// <summary>
    /// Passou do prazo de validade do tipo sem ser avaliado (N1, quarentena): o fato envelheceu e avisar agora seria
    /// ruído. Terminal; vai ao banco como texto (<c>varchar</c>), sem migration por valor novo.
    /// </summary>
    Expirado = 4
}
