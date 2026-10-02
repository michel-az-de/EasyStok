namespace EasyStock.Domain.Enums.Notifications;

public enum CategoriaConteudoNotificacao
{
    Transacional = 1,
    Operacional = 2,
    Marketing = 3,

    /// <summary>
    /// Mensagem que carrega credencial (reset de senha, confirmacao de cadastro, conta criada pelo admin): sai da
    /// caixa de seguranca (ADR-0057, item 2). Gravada como texto, sem migration.
    /// </summary>
    Seguranca = 4
}
