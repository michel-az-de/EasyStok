using System.Collections.Concurrent;
using EasyStock.Application.Ports.Output.Atendimento;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Registrado quando <c>Notifications:WhatsApp:Provider=stub</c>. Não bate na rede; grava cada
/// chamada em memória para os testes de integração inspecionarem o que foi "enviado".
/// </summary>
public sealed class StubWhatsAppCloudClient : IWhatsAppCloudClient
{
    private readonly ConcurrentQueue<ChamadaRegistrada> _chamadas = new();

    public IReadOnlyCollection<ChamadaRegistrada> Chamadas => _chamadas.ToArray();

    public Task<EnvioWhatsAppResult> EnviarTextoAsync(
        string waId, string texto, string? responderAWamid = null, CancellationToken ct = default)
        => Registrar("texto", waId, texto);

    public Task<EnvioWhatsAppResult> EnviarImagemAsync(
        string waId, string urlPublica, string? legenda = null, CancellationToken ct = default)
        => Registrar("imagem", waId, urlPublica);

    public Task<EnvioWhatsAppResult> EnviarBotoesAsync(
        string waId, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default)
        => Registrar("botoes", waId, corpo);

    public Task<EnvioWhatsAppResult> EnviarTemplateAsync(
        string waId,
        string nomeTemplate,
        string idioma,
        IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null,
        CancellationToken ct = default)
        => Registrar("template", waId, nomeTemplate);

    public Task MarcarComoLidaAsync(string wamid, CancellationToken ct = default)
    {
        _chamadas.Enqueue(new ChamadaRegistrada("marcar_lida", wamid, wamid, $"wamid-stub-{Guid.NewGuid():N}"));
        return Task.CompletedTask;
    }

    public Task<(Stream Conteudo, string MimeType)> BaixarMidiaAsync(string mediaId, CancellationToken ct = default)
    {
        _chamadas.Enqueue(new ChamadaRegistrada("baixar_midia", mediaId, mediaId, mediaId));
        return Task.FromResult<(Stream, string)>((new MemoryStream(), "application/octet-stream"));
    }

    private Task<EnvioWhatsAppResult> Registrar(string tipo, string waId, string conteudo)
    {
        var wamid = $"wamid-stub-{Guid.NewGuid():N}";
        _chamadas.Enqueue(new ChamadaRegistrada(tipo, waId, conteudo, wamid));
        return Task.FromResult(new EnvioWhatsAppResult(wamid));
    }
}

public sealed record ChamadaRegistrada(string Tipo, string Destinatario, string Conteudo, string Wamid);
