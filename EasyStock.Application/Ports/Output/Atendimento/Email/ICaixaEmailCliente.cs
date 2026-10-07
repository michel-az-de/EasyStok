namespace EasyStock.Application.Ports.Output.Atendimento.Email;

/// <summary>
/// Fala com a caixa de suporte da loja (#1432): IMAP para ler o que chegou, SMTP para responder em nome dela.
/// O adapter (MailKit) decide o TLS pela porta (993 e 465 implícito; o resto STARTTLS obrigatório) e nunca
/// manda a senha para o log.
/// </summary>
public interface ICaixaEmailCliente
{
    /// <summary>Entra no IMAP e no SMTP e sai, sem ler nem enviar nada.</summary>
    Task<ResultadoTesteCaixaEmail> TestarAsync(CaixaEmailAtendimento caixa, CancellationToken ct = default);

    /// <summary>Abre a INBOX para leitura e escrita de marcas. Falha de rede ou de login lança <see cref="FalhaCaixaEmailException"/>.</summary>
    Task<ISessaoCaixaEmail> AbrirAsync(CaixaEmailAtendimento caixa, CancellationToken ct = default);

    /// <summary>Envia pelo SMTP da caixa e devolve o Message-ID gerado (sem os sinais de menor e maior).</summary>
    Task<string> EnviarAsync(CaixaEmailAtendimento caixa, EmailSaida email, CancellationToken ct = default);
}

/// <summary>INBOX aberta. Ler não marca como lido: só <see cref="MarcarLidoAsync"/>, depois de gravar.</summary>
public interface ISessaoCaixaEmail : IAsyncDisposable
{
    /// <summary>Ids (UID do IMAP) dos não lidos, dos mais antigos para os mais novos, até <paramref name="maximo"/>.</summary>
    Task<IReadOnlyList<string>> ListarNaoLidosAsync(int maximo, CancellationToken ct = default);

    Task<EmailRecebido> BaixarAsync(string idNaCaixa, CancellationToken ct = default);

    Task MarcarLidoAsync(string idNaCaixa, CancellationToken ct = default);
}

/// <summary>
/// E-mail já lido do MIME. <see cref="DeEndereco"/> é o Reply-To quando houver (formulário que manda do
/// "noreply" com a resposta para o cliente), senão o From. <see cref="Texto"/> é o text/plain, ou o HTML
/// reduzido a texto; ainda com o histórico citado, que a aplicação corta.
/// </summary>
public sealed record EmailRecebido(
    string IdNaCaixa,
    string? MessageId,
    string DeEndereco,
    string? DeNome,
    string? Assunto,
    string Texto,
    DateTime RecebidoEm,
    bool AutoGerado,
    IReadOnlyList<AnexoEmailRecebido> Anexos);

/// <summary>Anexo do e-mail. <see cref="Conteudo"/> nulo quando passou do teto e não foi baixado.</summary>
public sealed record AnexoEmailRecebido(string NomeArquivo, string Mime, byte[]? Conteudo);

/// <summary>
/// Resposta da loja. <see cref="EmRespostaA"/> vira In-Reply-To e entra em References: é o que faz o
/// programa de e-mail do cliente juntar a resposta na mesma conversa.
/// </summary>
public sealed record EmailSaida(string Para, string Assunto, string Corpo, bool Html, string? EmRespostaA);

/// <summary>Resultado do "testar conexão": cada lado com o motivo curto da falha, sem endereço nem senha.</summary>
public sealed record ResultadoTesteCaixaEmail(bool ImapOk, string? ImapErro, bool SmtpOk, string? SmtpErro)
{
    public bool Ok => ImapOk && SmtpOk;
}

/// <summary>
/// A caixa recusou ou não respondeu. <see cref="Permanente"/>: login recusado ou destinatário recusado, que
/// tentar de novo não resolve.
/// </summary>
public sealed class FalhaCaixaEmailException(string mensagem, bool permanente, Exception? inner = null)
    : Exception(mensagem, inner)
{
    public bool Permanente { get; } = permanente;
}
