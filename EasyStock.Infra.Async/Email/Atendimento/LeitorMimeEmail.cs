using EasyStock.Application.Ports.Output.Atendimento.Email;
using MimeKit;

namespace EasyStock.Infra.Async.Email.Atendimento;

/// <summary>
/// MIME do e-mail recebido para o formato do atendimento (#1432). Responde para o Reply-To quando houver
/// (formulário do site manda do "noreply" com o cliente no Reply-To), senão para o From. Texto prefere o
/// text/plain; sem ele, o HTML reduzido a texto. Anexo é só o que vem marcado como anexo: imagem em linha da
/// assinatura não vira mensagem.
/// </summary>
internal static class LeitorMimeEmail
{
    /// <summary>Anexo maior que isto não é baixado: vira aviso na conversa.</summary>
    public const long TetoAnexoBytes = 15 * 1024 * 1024;

    /// <summary>E-mail inteiro maior que isto não é baixado: entra só o envelope (<see cref="SoEnvelope"/>).</summary>
    public const long TetoEmailBytes = 40 * 1024 * 1024;

    private static readonly string[] PrecedenciaAutomatica = ["bulk", "junk", "auto_reply"];

    public static EmailRecebido Ler(string idNaCaixa, MimeMessage mime, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(mime);

        var de = mime.ReplyTo.Mailboxes.FirstOrDefault() ?? mime.From.Mailboxes.FirstOrDefault() ?? mime.Sender;
        var nome = string.IsNullOrWhiteSpace(de?.Name) ? mime.From.Mailboxes.FirstOrDefault()?.Name : de.Name;
        var texto = !string.IsNullOrWhiteSpace(mime.TextBody) ? mime.TextBody : HtmlParaTexto.Converter(mime.HtmlBody);
        var data = mime.Date == DateTimeOffset.MinValue ? agora : mime.Date.UtcDateTime;

        return new EmailRecebido(
            idNaCaixa,
            string.IsNullOrWhiteSpace(mime.MessageId) ? null : mime.MessageId,
            de?.Address ?? string.Empty,
            string.IsNullOrWhiteSpace(nome) ? null : nome.Trim(),
            mime.Subject,
            texto ?? string.Empty,
            data,
            AutoGerado(mime),
            Anexos(mime));
    }

    /// <summary>
    /// E-mail grande demais para baixar: remetente, assunto e data do envelope do IMAP e um aviso no texto. Sem os
    /// cabeçalhos de resposta automática (o envelope não os traz): melhor uma conversa a mais que um cliente perdido.
    /// </summary>
    public static EmailRecebido SoEnvelope(string idNaCaixa, MailKit.Envelope envelope, ulong tamanho, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var de = envelope.ReplyTo.Mailboxes.FirstOrDefault() ?? envelope.From.Mailboxes.FirstOrDefault() ?? envelope.Sender.Mailboxes.FirstOrDefault();
        var megas = Math.Ceiling(tamanho / (1024d * 1024d));
        return new EmailRecebido(
            idNaCaixa,
            string.IsNullOrWhiteSpace(envelope.MessageId) ? null : envelope.MessageId,
            de?.Address ?? string.Empty,
            string.IsNullOrWhiteSpace(de?.Name) ? null : de.Name.Trim(),
            envelope.Subject,
            $"[e-mail de {megas} MB, grande demais para o EasyStok: abra na caixa da loja]",
            envelope.Date?.UtcDateTime ?? agora,
            AutoGerado: false,
            []);
    }

    /// <summary>Resposta automática (férias, aviso de entrega, devolução): RFC 3834 e os cabeçalhos de fato usados.</summary>
    internal static bool AutoGerado(MimeMessage mime)
    {
        var autoSubmitted = mime.Headers[HeaderId.AutoSubmitted]?.Trim();
        if (!string.IsNullOrEmpty(autoSubmitted) && !autoSubmitted.StartsWith("no", StringComparison.OrdinalIgnoreCase))
            return true;
        if (mime.Headers.Contains("X-Autoreply") || mime.Headers.Contains("X-Autorespond"))
            return true;
        var precedencia = mime.Headers[HeaderId.Precedence]?.Trim();
        return precedencia is not null && PrecedenciaAutomatica.Contains(precedencia, StringComparer.OrdinalIgnoreCase);
    }

    private static List<AnexoEmailRecebido> Anexos(MimeMessage mime)
    {
        var anexos = new List<AnexoEmailRecebido>();
        foreach (var entidade in mime.Attachments)
        {
            switch (entidade)
            {
                case MimePart parte:
                    anexos.Add(new AnexoEmailRecebido(
                        parte.FileName ?? parte.ContentType.Name ?? "anexo",
                        parte.ContentType.MimeType,
                        Conteudo(s => parte.Content?.DecodeTo(s))));
                    break;

                case MessagePart encaminhada:
                    anexos.Add(new AnexoEmailRecebido(
                        (encaminhada.Message?.Subject is { Length: > 0 } assunto ? assunto : "mensagem") + ".eml",
                        "message/rfc822",
                        Conteudo(s => encaminhada.Message?.WriteTo(s))));
                    break;
            }
        }
        return anexos;
    }

    private static byte[]? Conteudo(Action<Stream> escrever)
    {
        using var memoria = new MemoryStream();
        escrever(memoria);
        return memoria.Length > TetoAnexoBytes ? null : memoria.ToArray();
    }
}
