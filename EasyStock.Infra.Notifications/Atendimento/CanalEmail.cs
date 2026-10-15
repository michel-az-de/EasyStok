using System.Net;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Infra.Notifications.Atendimento;

/// <summary>
/// E-mail na porta de canal (S37, ADR-0051): texto simples, ou imagem por link em HTML, pelo
/// <see cref="IEmailService"/> configurado (<c>Email:Provider</c>). Sem botão e sem modelo.
/// </summary>
public sealed class CanalEmail(IEmailService email) : ICanalMensageria
{
    public const string Assunto = "Mensagem da loja";

    public CanalConversa Canal => CanalConversa.Email;

    public async Task<string> EnviarTextoAsync(string contatoIdExterno, string texto, CancellationToken ct = default)
    {
        await Enviar(() => email.SendAsync(contatoIdExterno, Assunto, texto, isHtml: false));
        return Guid.NewGuid().ToString("N");
    }

    public async Task<string> EnviarImagemAsync(string contatoIdExterno, string urlPublica, string? legenda = null, CancellationToken ct = default)
    {
        var corpo = $"<p><img src=\"{WebUtility.HtmlEncode(urlPublica)}\" alt=\"\" style=\"max-width:100%\"></p>"
            + (string.IsNullOrWhiteSpace(legenda) ? "" : $"<p>{WebUtility.HtmlEncode(legenda)}</p>");
        await Enviar(() => email.SendAsync(contatoIdExterno, Assunto, corpo, isHtml: true));
        return Guid.NewGuid().ToString("N");
    }

    public Task<string> EnviarBotoesAsync(string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        throw new NotSupportedException("E-mail não envia botões de resposta.");

    public Task<string> EnviarModeloAsync(string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, CancellationToken ct = default) =>
        throw new NotSupportedException("E-mail não tem modelo aprovado: não tem janela.");

    public Task MarcarComoLidaAsync(string idMensagemExterna, CancellationToken ct = default) => Task.CompletedTask;

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
