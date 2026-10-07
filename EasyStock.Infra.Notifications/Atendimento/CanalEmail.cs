using System.Net;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Atendimento.Email;
using EasyStock.Application.Services.Atendimento.Email;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Infra.Notifications.Atendimento;

/// <summary>
/// E-mail na porta de canal (S37, ADR-0051): texto simples, ou imagem por link em HTML. Sem botão e sem modelo.
/// <para>
/// Com caixa de suporte configurada na empresa (#1432), sai pelo SMTP dela, em nome dela, com "Re: assunto" e
/// In-Reply-To do último e-mail recebido da conversa, e o id externo é o Message-ID gerado. Sem caixa, sai pelo
/// <see cref="IEmailService"/> da plataforma (<c>Email:Provider</c>), como antes, com assunto fixo.
/// </para>
/// </summary>
public sealed class CanalEmail(
    IEmailService email,
    ICaixaEmailDoTenant? caixaDoTenant = null,
    ICaixaEmailCliente? caixaCliente = null) : ICanalMensageria
{
    public const string Assunto = AssuntoEmail.Padrao;

    public CanalConversa Canal => CanalConversa.Email;

    public async Task<string> EnviarTextoAsync(string contatoIdExterno, string texto, CancellationToken ct = default)
    {
        if (await CaixaAsync(ct) is { } caixa)
            return await PelaCaixaAsync(caixa, contatoIdExterno, texto, html: false, ct);

        await Enviar(() => email.SendAsync(contatoIdExterno, Assunto, texto, isHtml: false));
        return Guid.NewGuid().ToString("N");
    }

    public async Task<string> EnviarImagemAsync(string contatoIdExterno, string urlPublica, string? legenda = null, CancellationToken ct = default)
    {
        var corpo = $"<p><img src=\"{WebUtility.HtmlEncode(urlPublica)}\" alt=\"\" style=\"max-width:100%\"></p>"
            + (string.IsNullOrWhiteSpace(legenda) ? "" : $"<p>{WebUtility.HtmlEncode(legenda)}</p>");
        if (await CaixaAsync(ct) is { } caixa)
            return await PelaCaixaAsync(caixa, contatoIdExterno, corpo, html: true, ct);

        await Enviar(() => email.SendAsync(contatoIdExterno, Assunto, corpo, isHtml: true));
        return Guid.NewGuid().ToString("N");
    }

    public Task<string> EnviarBotoesAsync(string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        throw new NotSupportedException("E-mail não envia botões de resposta.");

    public Task<string> EnviarModeloAsync(string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, CancellationToken ct = default) =>
        throw new NotSupportedException("E-mail não tem modelo aprovado: não tem janela.");

    public Task MarcarComoLidaAsync(string idMensagemExterna, CancellationToken ct = default) => Task.CompletedTask;

    private async Task<CaixaEmailAtendimento?> CaixaAsync(CancellationToken ct) =>
        caixaDoTenant is null || caixaCliente is null ? null : await caixaDoTenant.ObterCaixaAsync(ct);

    private async Task<string> PelaCaixaAsync(
        CaixaEmailAtendimento caixa, string para, string corpo, bool html, CancellationToken ct)
    {
        var fio = await caixaDoTenant!.ObterFioAsync(para, ct);
        var saida = new EmailSaida(para, AssuntoEmail.Resposta(fio?.Assunto), corpo, html, fio?.UltimoMessageIdRecebido);
        try
        {
            return await caixaCliente!.EnviarAsync(caixa, saida, ct);
        }
        catch (FalhaCaixaEmailException ex)
        {
            throw new EnvioCanalFalhouException($"Falha no envio de e-mail pela caixa da loja: {ex.Message}", ex.Permanente);
        }
    }

    private static async Task Enviar(Func<Task> envio)
    {
        try
        {
            await envio();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new EnvioCanalFalhouException($"Falha no envio de e-mail: {ex.Message}", falhaPermanente: false);
        }
    }
}
