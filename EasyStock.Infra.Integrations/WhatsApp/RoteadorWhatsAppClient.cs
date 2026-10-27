using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Integrations.WhatsApp;

// A sessão é opt-in por empresa. Sem ela, exige Meta real; o piloto nunca confirma envio por stub.
public sealed class RoteadorWhatsAppClient(IRemetenteWhatsApp remetente, WppConnectClient web,
    WhatsAppCloudClient meta, IConfiguration configuration) : IWhatsAppCloudClient
{
    private async Task<IWhatsAppCloudClient> ClienteAsync(CancellationToken ct) =>
        await remetente.ObterSessaoWebAsync(ct) is not null ? web :
        WhatsAppCloudClientServiceCollectionExtensions.UsaClienteMeta(
            configuration[WhatsAppCloudClientServiceCollectionExtensions.ChaveClienteAtendimento],
            configuration[WhatsAppCloudClientServiceCollectionExtensions.ChaveProviderNotificacoes]) ? meta
            : throw new WhatsAppCloudException(-2071, "Conecte o WhatsApp da empresa antes de enviar mensagens.", true);

    public async Task<EnvioWhatsAppResult> EnviarTextoAsync(string waId, string texto, string? responderAWamid = null, CancellationToken ct = default) =>
        await (await ClienteAsync(ct)).EnviarTextoAsync(waId, texto, responderAWamid, ct);
    public async Task<EnvioWhatsAppResult> EnviarImagemAsync(string waId, string urlPublica, string? legenda = null, CancellationToken ct = default) =>
        await (await ClienteAsync(ct)).EnviarImagemAsync(waId, urlPublica, legenda, ct);
    public async Task<EnvioWhatsAppResult> EnviarAudioAsync(string waId, string urlPublica, bool notaDeVoz, CancellationToken ct = default) =>
        await (await ClienteAsync(ct)).EnviarAudioAsync(waId, urlPublica, notaDeVoz, ct);
    public async Task<EnvioWhatsAppResult> EnviarBotoesAsync(string waId, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        await (await ClienteAsync(ct)).EnviarBotoesAsync(waId, corpo, botoes, ct);
    public async Task<EnvioWhatsAppResult> EnviarTemplateAsync(string waId, string nomeTemplate, string idioma, IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null, string? imagemCabecalho = null, CancellationToken ct = default) =>
        await (await ClienteAsync(ct)).EnviarTemplateAsync(waId, nomeTemplate, idioma, parametrosCorpo, botoesQuickReply, imagemCabecalho, ct);
    public async Task MarcarComoLidaAsync(string wamid, CancellationToken ct = default) =>
        await (await ClienteAsync(ct)).MarcarComoLidaAsync(wamid, ct);
    public async Task<(Stream Conteudo, string MimeType)> BaixarMidiaAsync(string mediaId, CancellationToken ct = default) =>
        await (await ClienteAsync(ct)).BaixarMidiaAsync(mediaId, ct);
}
