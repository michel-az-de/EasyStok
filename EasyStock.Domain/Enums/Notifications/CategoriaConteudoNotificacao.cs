namespace EasyStock.Domain.Enums.Notifications;

public enum CategoriaConteudoNotificacao
{
    Transacional = 1,
    Operacional = 2,
    Marketing = 3,

    /// <summary>
    /// Segurança da conta (redefinição de senha, código de acesso). Ignora consentimento como a transacional e,
    /// ao terminar, a mensagem apaga corpo, assunto e metadados, e o evento apaga o payload (N2, ADR-0057).
    /// </summary>
    Seguranca = 4
}

public static class CategoriaConteudoNotificacaoExtensions
{
    /// <summary>
    /// Transacional (interesse legítimo) e <see cref="CategoriaConteudoNotificacao.Seguranca"/> (segurança da
    /// conta) saem sem opt-in e ignoram opt-out. É a mesma regra do <c>ResolvedorCanal</c> e do
    /// <c>BypassConsentimento</c> do log de envio.
    /// </summary>
    public static bool IgnoraConsentimento(this CategoriaConteudoNotificacao categoria) =>
        categoria is CategoriaConteudoNotificacao.Transacional or CategoriaConteudoNotificacao.Seguranca;
}
