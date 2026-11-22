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

public sealed class WppConnectClient(HttpClient http, IOptions<WppConnectOptions> options, IRemetenteWhatsApp remetente,
    BaixadorFotoPerfilWhatsApp? fotos = null, IMenusNumeradosWppConnect? menus = null)
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

    /// <summary>#2590: as últimas mensagens da conversa, para recuperar o id que o evento perdeu.</summary>
    public async Task<IReadOnlyList<MensagemRecenteWppConnect>> ListarMensagensRecentesAsync(CredencialWppConnect sessao, string chat,
        CancellationToken ct = default)
    {
        using var doc = await ChamarAsync(HttpMethod.Get, Path(sessao, $"get-messages/{Uri.EscapeDataString(chat)}?count=30"), sessao, null, ct);
        var lista = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("response", out var r) ? r : default;
        var itens = lista.ValueKind switch
        {
            JsonValueKind.Array => lista.EnumerateArray().ToList(),
            JsonValueKind.Object => lista.EnumerateObject().Select(p => p.Value).ToList(),
            _ => []
        };
        return [.. itens.Select(Recente).OfType<MensagemRecenteWppConnect>()];
    }

    /// <summary>
    /// #3117: as conversas individuais mais recentes e a última mensagem recebida de cada uma (<c>lastReceivedKey</c>).
    /// O servidor responde a lista direta; a forma com <c>response</c> também é aceita. Conversa sem a chave, de grupo,
    /// de transmissão ou cuja última recebida é da própria loja fica de fora. Formato inesperado vira lista vazia.
    /// </summary>
    public async Task<IReadOnlyList<UltimaRecebidaWppConnect>> ListarUltimasRecebidasAsync(CredencialWppConnect sessao, int maximo,
        CancellationToken ct = default)
    {
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, "list-chats"), sessao,
            new { count = maximo, onlyUsers = true }, ct);
        var lista = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("response", out var r)
            ? r : doc.RootElement;
        if (lista.ValueKind != JsonValueKind.Array) return [];
        return [.. lista.EnumerateArray().Select(UltimaRecebida).OfType<UltimaRecebidaWppConnect>().Take(maximo)];
    }

    private static UltimaRecebidaWppConnect? UltimaRecebida(JsonElement conversa)
    {
        if (conversa.ValueKind != JsonValueKind.Object
            || !conversa.TryGetProperty("id", out var id) || Identificador(id) is not { Length: > 0 } chat
            || chat.EndsWith("@g.us", StringComparison.Ordinal) || chat.EndsWith("@broadcast", StringComparison.Ordinal)
            || !conversa.TryGetProperty("lastReceivedKey", out var chave) || chave.ValueKind != JsonValueKind.Object
            || (chave.TryGetProperty("fromMe", out var deMim) && deMim.ValueKind == JsonValueKind.True)
            || Identificador(chave) is not { Length: > 0 } mensagem)
            return null;
        return new(chat, mensagem);
    }

    private static MensagemRecenteWppConnect? Recente(JsonElement m) =>
        m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && Identificador(id) is { Length: > 0 } valor
            && m.TryGetProperty("t", out var t) && t.TryGetInt64(out var quando)
            ? new(valor, quando, m.TryGetProperty("fromMe", out var f) && f.ValueKind == JsonValueKind.True,
                LerTexto(m, "type"), LerTexto(m, "body"))
            : null;

    public async Task<EnvioWhatsAppResult> EnviarTextoAsync(string waId, string texto, string? responderAWamid = null, CancellationToken ct = default) =>
        await EnviarAsync("send-message", new { phone = waId, isGroup = false, message = texto }, ct);

    public Task<EnvioWhatsAppResult> EnviarImagemAsync(string waId, string urlPublica, string? legenda = null, CancellationToken ct = default) =>
        EnviarArquivoAsync(waId, urlPublica, legenda, false, ct);

    public Task<EnvioWhatsAppResult> EnviarDocumentoAsync(
        string waId, string urlPublica, string nomeArquivo, string? legenda = null, CancellationToken ct = default) =>
        EnviarArquivoAsync(waId, urlPublica, legenda, false, ct, nomeArquivo);

    public Task<EnvioWhatsAppResult> EnviarAudioAsync(string waId, string urlPublica, bool notaDeVoz, CancellationToken ct = default) =>
        EnviarArquivoAsync(waId, urlPublica, null, notaDeVoz, ct);

    private Task<EnvioWhatsAppResult> EnviarArquivoAsync(string waId, string url, string? legenda, bool voz, CancellationToken ct, string? nomeArquivo = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A mídia precisa de URL HTTP(S) acessível pelo conector.", nameof(url));
        return EnviarAsync(voz ? "send-voice" : "send-file-base64",
            new { phone = waId, isGroup = false, path = url, caption = legenda, filename = voz ? "audio.ogg" : nomeArquivo ?? System.IO.Path.GetFileName(uri.AbsolutePath) }, ct);
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

    /// <summary>Conta comum não tem botão: as opções saem numeradas e o menu fica guardado para a resposta (#2515).</summary>
    public async Task<EnvioWhatsAppResult> EnviarBotoesAsync(string waId, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default)
    {
        var envio = await EnviarTextoAsync(waId, MenuNumeradoWhatsApp.Texto(corpo, botoes.Select(b => b.Titulo)), ct: ct);
        if (menus is not null)
            await menus.GuardarAsync((await SessaoAsync(ct)).Sessao, waId, botoes, ct);
        return envio;
    }

    public Task<EnvioWhatsAppResult> EnviarTemplateAsync(string waId, string nomeTemplate, string idioma, IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null, string? imagemCabecalho = null, CancellationToken ct = default) =>
        throw Falha("Modelos da Meta não estão disponíveis na conexão provisória. Responda com texto pelo chat.", permanente: true);

    public async Task MarcarComoLidaAsync(string wamid, CancellationToken ct = default)
    {
        if (LerIdMensagem(wamid) is not { } msg) return;
        var sessao = await SessaoAsync(ct);
        if (msg.Sessao != sessao.Sessao) throw Falha("A mensagem não pertence à sessão desta empresa.", permanente: true);
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, "send-seen"), sessao,
            new { phone = msg.Conversa.Split('@')[0], isGroup = false, isLid = msg.EhLid }, ct);
    }

    /// <summary>#2833: <c>react-message</c> pelo id serializado; <c>false</c> retira a reação.</summary>
    public async Task ReagirAsync(string waId, string wamid, string? emoji, CancellationToken ct = default)
    {
        var (sessao, msg) = await MensagemDaSessaoAsync(wamid, ct);
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, "react-message"), sessao,
            new { msgId = msg.Serializado, reaction = (object?)emoji ?? false }, ct);
        ExigirSucesso(doc, "O WPPConnect não confirmou a reação.");
    }

    /// <summary>#2833: <c>delete-message</c> não completa o telefone, então vai a conversa inteira (<c>@c.us</c> ou <c>@lid</c>).</summary>
    public async Task ApagarParaTodosAsync(string waId, string wamid, CancellationToken ct = default)
    {
        var (sessao, msg) = await MensagemDaSessaoAsync(wamid, ct);
        using var doc = await ChamarAsync(HttpMethod.Post, Path(sessao, "delete-message"), sessao,
            new { phone = msg.Conversa, isGroup = false, messageId = msg.Serializado, onlyLocal = false, deleteMediaInDevice = false }, ct);
        ExigirSucesso(doc, "O WPPConnect não confirmou que apagou a mensagem.");
    }

    private async Task<(CredencialWppConnect Sessao, IdMensagemWpp Msg)> MensagemDaSessaoAsync(string wamid, CancellationToken ct)
    {
        var msg = LerIdMensagem(wamid)
                  ?? throw new AcaoNaoSuportadaWhatsAppException("Esta mensagem não saiu pela conexão por QR; por ela não dá para reagir nem apagar.");
        var sessao = await SessaoAsync(ct);
        if (msg.Sessao != sessao.Sessao) throw Falha("A mensagem não pertence à sessão desta empresa.", permanente: true);
        return (sessao, msg);
    }

    private static void ExigirSucesso(JsonDocument doc, string erro)
    {
        if (LerTexto(doc.RootElement, "status") != "success") throw Falha(erro, 502);
    }

    private sealed record IdMensagemWpp(string Sessao, string Serializado, string Conversa)
    {
        public bool EhLid => Conversa.EndsWith("@lid", StringComparison.Ordinal);
    }

    /// <summary>Id externo <c>wpp:{sessao}:{deMim}_{conversa}_{id}</c>; nulo fora desse formato ou de conversa em grupo.</summary>
    private static IdMensagemWpp? LerIdMensagem(string wamid)
    {
        var partes = wamid.Split(':', 3);
        if (partes.Length != 3 || partes[0] != "wpp") return null;
        var id = partes[2].Split('_');
        if (id.Length < 3) return null;
        var conversa = id[1];
        if (!conversa.EndsWith("@lid", StringComparison.Ordinal) && !conversa.EndsWith("@c.us", StringComparison.Ordinal)) return null;
        return new(partes[1], partes[2], conversa);
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

    /// <summary>#2155: <c>profile-pic/{telefone}</c> devolve o endereço da foto no CDN (<c>eurl</c>), ou nada sem foto visível.</summary>
    public async Task<FotoPerfilWhatsApp?> ObterFotoPerfilAsync(string waId, CancellationToken ct = default)
    {
        if (fotos is null) return null;
        var sessao = await SessaoAsync(ct);
        using var doc = await ChamarAsync(HttpMethod.Get, Path(sessao, $"profile-pic/{Uri.EscapeDataString(waId)}"), sessao, null, ct);
        var dados = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("response", out var r) ? r : default;
        return await fotos.BaixarAsync(LerTexto(dados, "eurl") ?? LerTexto(dados, "imgFull"), ct);
    }

    private static string? LerTexto(JsonElement root, string nome) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static string? Identificador(JsonElement valor) =>
        valor.ValueKind == JsonValueKind.String ? valor.GetString() : LerTexto(valor, "_serialized");
}
