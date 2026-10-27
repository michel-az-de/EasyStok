using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.WhatsApp;

public sealed class WppConnectOptions
{
    public bool Habilitado { get; set; }
    public string BaseUrl { get; set; } = "http://127.0.0.1:21465/";
    public string SecretKey { get; set; } = "";
    public string WebhookToken { get; set; } = "";
}

public sealed class WppConnectClient(HttpClient http, IOptions<WppConnectOptions> options, IRemetenteWhatsApp remetente)
    : IWhatsAppCloudClient, IWppConnectSessaoClient
{
    private readonly WppConnectOptions _options = options.Value;
    public bool Habilitado => _options.Habilitado && !string.IsNullOrWhiteSpace(_options.SecretKey)
        && _options.WebhookToken.Length >= 16;
    private static WhatsAppCloudException Falha(string texto, int? status = null, bool permanente = false) =>
        new(-2071, texto, permanente, status);

    private async Task<CredencialWppConnect> SessaoAsync(CancellationToken ct)
    {
        if (!Habilitado) throw Falha("A conexão provisória do WhatsApp está desabilitada.", permanente: true);
        return await remetente.ObterSessaoWebAsync(ct)
            ?? throw Falha("Esta empresa não possui sessão WPPConnect.", permanente: true);
    }

    // Sem retry: uma queda depois do POST pode ocorrer depois de o WhatsApp aceitar a mensagem.
    private async Task<JsonDocument> ChamarAsync(HttpMethod metodo, string caminho, CredencialWppConnect? sessao,
        object? corpo, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(metodo, caminho);
        if (sessao is not null) request.Headers.Authorization = new("Bearer", sessao.Token);
        if (corpo is not null) request.Content = JsonContent.Create(corpo);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw Falha("O WPPConnect recusou a operação. Confira a conexão antes de tentar novamente.",
                (int)response.StatusCode, response.StatusCode is System.Net.HttpStatusCode.BadRequest
                    or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden);
        if (response.Content.Headers.ContentLength > 30_000_000) throw new MidiaGrandeDemaisException(30_000_000);
        // LoadIntoBufferAsync impõe o teto também quando o servidor não informa Content-Length.
        await response.Content.LoadIntoBufferAsync(30_000_000, ct);
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw Falha("O WPPConnect devolveu uma resposta inválida.", 502); }
    }

    private static string Path(CredencialWppConnect sessao, string acao) =>
        $"api/{Uri.EscapeDataString(sessao.Sessao)}/{acao}";

    public async Task<string> GerarTokenAsync(string sessao, CancellationToken ct = default)
    {
        if (!Habilitado || CredencialWppConnect.EmpresaDaSessao(sessao) is null)
            throw Falha("A conexão provisória não está configurada.", permanente: true);
        using var doc = await ChamarAsync(HttpMethod.Post,
            $"api/{Uri.EscapeDataString(sessao)}/{Uri.EscapeDataString(_options.SecretKey)}/generate-token", null, null, ct);
        return LerTexto(doc.RootElement, "token") ?? throw Falha("O WPPConnect não gerou o token da sessão.", 502);
    }

    public async Task IniciarAsync(CredencialWppConnect sessao, CancellationToken ct = default)
    {
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, "start-session"), sessao, new { waitQrCode = false }, ct);
    }

    public async Task FecharAsync(CredencialWppConnect sessao, bool sair, CancellationToken ct = default)
    {
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, sair ? "logout-session" : "close-session"), sessao, null, ct);
        if (!doc.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.True)
            throw Falha("O WPPConnect não confirmou o encerramento da sessão.", 502);
    }

    public async Task<EstadoWppConnect> ObterEstadoAsync(CredencialWppConnect sessao, CancellationToken ct = default)
    {
        using var doc = await ChamarAsync(HttpMethod.Get, Path(sessao, "status-session"), sessao, null, ct);
        var estado = LerTexto(doc.RootElement, "status") ?? "DESCONHECIDO";
        // CONNECTED precisa ser confirmado pela conexão do cliente, não apenas pelo estado em memória.
        var conectado = false;
        if (estado == "CONNECTED")
        {
            using var check = await ChamarAsync(HttpMethod.Get, Path(sessao, "check-connection-session"), sessao, null, ct);
            conectado = check.RootElement.TryGetProperty("status", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        var qr = conectado ? null : LerTexto(doc.RootElement, "qrcode");
        if (qr is not null && !qr.StartsWith("data:image/png;base64,", StringComparison.Ordinal)) qr = null;
        return new(Habilitado, true, conectado, estado, qr);
    }

    public async Task<string> ResolverTelefoneAsync(CredencialWppConnect sessao, string lid, CancellationToken ct = default)
    {
        using var doc = await ChamarAsync(HttpMethod.Get, Path(sessao, $"contact/pn-lid/{Uri.EscapeDataString(lid)}"), sessao, null, ct);
        var pn = doc.RootElement.TryGetProperty("phoneNumber", out var valor) ? Identificador(valor) : null;
        if (pn is null || !pn.EndsWith("@c.us", StringComparison.Ordinal))
            throw Falha("O WhatsApp ainda não resolveu o telefone deste contato. O evento será tentado novamente.", 503);
        return pn[..^5];
    }

    public async Task<EnvioWhatsAppResult> EnviarTextoAsync(string waId, string texto, string? responderAWamid = null, CancellationToken ct = default) =>
        await EnviarAsync("send-message", new { phone = waId, isGroup = false, message = texto }, ct);

    public Task<EnvioWhatsAppResult> EnviarImagemAsync(string waId, string urlPublica, string? legenda = null, CancellationToken ct = default) =>
        EnviarArquivoAsync(waId, urlPublica, legenda, false, ct);

    public Task<EnvioWhatsAppResult> EnviarAudioAsync(string waId, string urlPublica, bool notaDeVoz, CancellationToken ct = default) =>
        EnviarArquivoAsync(waId, urlPublica, null, notaDeVoz, ct);

    private Task<EnvioWhatsAppResult> EnviarArquivoAsync(string waId, string url, string? legenda, bool voz, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A mídia precisa de URL HTTP(S) acessível pelo conector.", nameof(url));
        return EnviarAsync(voz ? "send-voice" : "send-file-base64",
            new { phone = waId, isGroup = false, path = url, caption = legenda, filename = voz ? "audio.ogg" : System.IO.Path.GetFileName(uri.AbsolutePath) }, ct);
    }

    private async Task<EnvioWhatsAppResult> EnviarAsync(string acao, object corpo, CancellationToken ct)
    {
        var sessao = await SessaoAsync(ct);
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, acao), sessao, corpo, ct);
        var root = doc.RootElement;
        if (LerTexto(root, "status") != "success" || !root.TryGetProperty("response", out var result))
            throw Falha("O WPPConnect não confirmou o envio. Confira o celular antes de repetir.", 502);
        if (result.ValueKind == JsonValueKind.Array)
        {
            if (result.GetArrayLength() != 1) throw Falha("O WPPConnect não confirmou a mensagem.", 502);
            result = result[0];
        }
        var id = result.TryGetProperty("id", out var valor) ? Identificador(valor) : null;
        if (string.IsNullOrWhiteSpace(id)) throw Falha("Envio sem identificador; confira o celular antes de repetir.", 502);
        return new(sessao.IdExterno(id));
    }

    public Task<EnvioWhatsAppResult> EnviarBotoesAsync(string waId, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default) =>
        EnviarTextoAsync(waId, corpo + "\n" + string.Join("\n", botoes.Select((b, i) => $"{i + 1}. {b.Titulo}")), ct: ct);

    public Task<EnvioWhatsAppResult> EnviarTemplateAsync(string waId, string nomeTemplate, string idioma, IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null, string? imagemCabecalho = null, CancellationToken ct = default) =>
        throw Falha("Modelos da Meta não estão disponíveis na conexão provisória. Responda com texto pelo chat.", permanente: true);

    public async Task MarcarComoLidaAsync(string wamid, CancellationToken ct = default)
    {
        var partes = wamid.Split(':', 3);
        if (partes.Length != 3 || partes[0] != "wpp") return;
        var id = partes[2].Split('_');
        if (id.Length < 3) return;
        var remoto = id[1];
        var lid = remoto.EndsWith("@lid", StringComparison.Ordinal);
        if (!lid && !remoto.EndsWith("@c.us", StringComparison.Ordinal)) return;
        var sessao = await SessaoAsync(ct);
        if (partes[1] != sessao.Sessao) throw Falha("A mensagem não pertence à sessão desta empresa.", permanente: true);
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, "send-seen"), sessao,
            new { phone = remoto.Split('@')[0], isGroup = false, isLid = lid }, ct);
    }

    public async Task<(Stream Conteudo, string MimeType)> BaixarMidiaAsync(string mediaId, CancellationToken ct = default)
    {
        var sessao = await SessaoAsync(ct);
        if (!mediaId.StartsWith("wpp:", StringComparison.Ordinal)) throw Falha("Identificador de mídia incompatível.", permanente: true);
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, "download-media"), sessao, new { messageId = mediaId[4..] }, ct);
        var base64 = LerTexto(doc.RootElement, "base64") ?? throw Falha("Mídia indisponível no WhatsApp.", 502);
        var mime = LerTexto(doc.RootElement, "mimetype") ?? "application/octet-stream";
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { throw Falha("O WPPConnect devolveu mídia inválida.", 502); }
        return (new MemoryStream(bytes, writable: false), mime);
    }

    private static string? LerTexto(JsonElement root, string nome) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string? Identificador(JsonElement valor) =>
        valor.ValueKind == JsonValueKind.String ? valor.GetString() : LerTexto(valor, "_serialized");
}
