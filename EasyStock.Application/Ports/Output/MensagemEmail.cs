namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Caixa de onde o e-mail sai (ADR-0057, item 2). <see cref="Avisos"/> e o padrao; <see cref="Seguranca"/>
/// carrega credencial (reset de senha, confirmacao de cadastro, conta criada pelo admin) e nao deve
/// dividir caixa nem reputacao com relatorio e aviso.
/// </summary>
public enum RemetenteEmail
{
    Avisos = 0,
    Seguranca = 1,
}

/// <summary>
/// Mensagem de e-mail entregue ao <see cref="IEmailService.EnviarAsync"/>. <paramref name="OutboxId"/> e
/// so o rastro de log (LGPD: o log nunca leva o endereco do destinatario).
/// </summary>
public sealed record MensagemEmail(
    string Destinatario,
    string Assunto,
    string Corpo,
    bool Html = false,
    IReadOnlyList<EmailAttachment>? Anexos = null,
    RemetenteEmail Remetente = RemetenteEmail.Avisos,
    Guid? OutboxId = null);
