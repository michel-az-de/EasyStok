using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>Adaptador do WhatsApp na porta de canal (S34): traduz para o <see cref="IWhatsAppCloudClient"/> (S02).</summary>
public sealed class CanalWhatsApp(IWhatsAppCloudClient cloud) : ICanalMensageria
{
    public CanalConversa Canal => CanalConversa.WhatsApp;

    public async Task<string> EnviarTextoAsync(string contatoIdExterno, string texto, CancellationToken ct = default) =>
        (await cloud.EnviarTextoAsync(contatoIdExterno, texto, null, ct)).Wamid;

    public async Task<string> EnviarImagemAsync(string contatoIdExterno, string urlPublica, string? legenda = null, CancellationToken ct = default) =>
        (await cloud.EnviarImagemAsync(contatoIdExterno, urlPublica, legenda, ct)).Wamid;

    public async Task<string> EnviarBotoesAsync(
        string contatoIdExterno, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        (await cloud.EnviarBotoesAsync(contatoIdExterno, corpo, botoes, ct)).Wamid;

    public async Task<string> EnviarModeloAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, CancellationToken ct = default) =>
        (await cloud.EnviarTemplateAsync(contatoIdExterno, nome, idioma, parametros, null, null, ct)).Wamid;

    public async Task<string> EnviarModeloAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros,
        IReadOnlyList<(string Id, string Titulo)>? botoes, CancellationToken ct = default) =>
        (await cloud.EnviarTemplateAsync(contatoIdExterno, nome, idioma, parametros, botoes, null, ct)).Wamid;

    public async Task<string> EnviarModeloComImagemAsync(
        string contatoIdExterno, string nome, string idioma, IReadOnlyList<string> parametros, string urlImagem,
        CancellationToken ct = default) =>
        (await cloud.EnviarTemplateAsync(contatoIdExterno, nome, idioma, parametros, null, urlImagem, ct)).Wamid;

    public Task MarcarComoLidaAsync(string idMensagemExterna, CancellationToken ct = default) =>
        cloud.MarcarComoLidaAsync(idMensagemExterna, ct);
}
