using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;
using Polly.Timeout;

namespace EasyStock.Infra.Integrations.WhatsApp.Waha;

/// <summary>
/// Ponte provisória do WhatsApp pelo WAHA (#2072, ADR-0062): o número da loja fica no celular e o WAHA é um aparelho
/// vinculado a ele. Fala a mesma porta da Cloud API, então conversa, agente, outbox e console não mudam.
/// <list type="bullet">
/// <item>Antes de cada envio, "digitando" e uma pausa proporcional ao texto; um envio por vez no processo.</item>
/// <item>Botões viram opções numeradas no texto (<see cref="MenuNumeradoWaha"/>).</item>
/// <item>Modelo aprovado não existe aqui: recusa permanente, com o motivo para quem estiver no console.</item>
/// <item>Só envia para a empresa vinculada ao número da ponte, nunca pelo número de outra.</item>
/// </list>
/// O <c>POST</c> de envio passa pelo pipeline sem retry (<see cref="IntegrationCategories.WahaEnvio"/>), como na
/// Meta: timeout depois de o WAHA receber deixaria a mensagem em dobro.
/// </summary>
public sealed class WahaWhatsAppClient(
    HttpClient http,
    IOptions<WahaOptions> options,
    IRemetenteWhatsApp remetente,
    ResolvedorContatoWaha resolvedor,
    MenuNumeradoWaha menus,
    ResiliencePipelineProvider<string> pipelines,
    TimeProvider relogio,
    ILogger<WahaWhatsAppClient> logger,
    BaixadorFotoPerfilWhatsApp? fotos = null) : IWhatsAppCloudClient
{
    public const string MotivoSemModelo =
        "A ponte do WhatsApp não envia modelo aprovado. Responda pelo celular da loja ou espere o cliente escrever.";

    private static readonly SemaphoreSlim UmEnvioPorVez = new(1, 1);

    private static readonly TimeSpan TempoMaximoDigitando = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly WahaOptions _opcoes = options.Value;

    public async Task<EnvioWhatsAppResult> EnviarTextoAsync(
        string waId, string texto, string? responderAWamid = null, CancellationToken ct = default)
    {
        var conversa = await ConversaDaEmpresaAsync(waId, ct);
        return await EnviarAsync("api/sendText", conversa, texto.Length,
            new { session = _opcoes.Sessao, chatId = conversa, text = texto, reply_to = responderAWamid }, ct);
    }

    public async Task<EnvioWhatsAppResult> EnviarImagemAsync(
        string waId, string urlPublica, string? legenda = null, CancellationToken ct = default)
    {
        ExigirUrlAbsoluta(urlPublica);
        var conversa = await ConversaDaEmpresaAsync(waId, ct);
        var arquivo = new { mimetype = MimeDaImagem(urlPublica), url = urlPublica, filename = NomeDoArquivo(urlPublica) };
        return await EnviarAsync("api/sendImage", conversa, legenda?.Length ?? 0,
            new { session = _opcoes.Sessao, chatId = conversa, file = arquivo, caption = legenda }, ct);
    }

    /// <summary>Sai como mensagem de voz. Ogg/Opus marcado como nota de voz vai como está; o resto o WAHA converte.</summary>
    public async Task<EnvioWhatsAppResult> EnviarAudioAsync(
        string waId, string urlPublica, bool notaDeVoz, CancellationToken ct = default)
    {
        ExigirUrlAbsoluta(urlPublica);
        var conversa = await ConversaDaEmpresaAsync(waId, ct);
        var arquivo = new { mimetype = notaDeVoz ? "audio/ogg; codecs=opus" : MimeDoAudio(urlPublica), url = urlPublica };
        return await EnviarAsync("api/sendVoice", conversa, 0,
            new { session = _opcoes.Sessao, chatId = conversa, file = arquivo, convert = !notaDeVoz }, ct);
    }

    public async Task<EnvioWhatsAppResult> EnviarBotoesAsync(
        string waId, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default)
    {
        // As mesmas regras da Cloud API: quem chama não muda de comportamento conforme o canal.
        if (botoes.Count is 0 or > 3)
            throw new ArgumentException("EnviarBotoesAsync aceita de 1 a 3 botões.", nameof(botoes));
        foreach (var (id, titulo) in botoes)
        {
            if (titulo.Length > 20)
                throw new ArgumentException($"Título de botão excede 20 caracteres: \"{titulo}\".", nameof(botoes));
            if (id.Length > 256)
                throw new ArgumentException($"Id de botão excede 256 caracteres: \"{id}\".", nameof(botoes));
        }

        var conversa = await ConversaDaEmpresaAsync(waId, ct);
        var texto = MenuNumeradoWaha.Formatar(corpo, botoes);
        var envio = await EnviarAsync("api/sendText", conversa, texto.Length,
            new { session = _opcoes.Sessao, chatId = conversa, text = texto }, ct);
        await menus.GuardarAsync(conversa, botoes, ct);
        return envio;
    }

    public Task<EnvioWhatsAppResult> EnviarTemplateAsync(
        string waId,
        string nomeTemplate,
        string idioma,
        IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null,
        string? imagemCabecalho = null,
        CancellationToken ct = default) =>
        Task.FromException<EnvioWhatsAppResult>(new WhatsAppCloudException(0, MotivoSemModelo, ehPermanente: true));

    public async Task MarcarComoLidaAsync(string wamid, CancellationToken ct = default)
    {
        var id = WahaIds.LerIdMensagem(wamid);
        if (id is null || id.DeMim)
        {
            logger.LogDebug("Ponte WAHA: id fora do formato do WAHA, leitura não marcada.");
            return;
        }

        await GarantirEmpresaDaPonteAsync(ct);
        using var resposta = await http.PostAsync("api/sendSeen",
            JsonContent.Create(new { session = _opcoes.Sessao, chatId = id.Conversa, messageIds = new[] { wamid } }, options: Json), ct);
        if (!resposta.IsSuccessStatusCode)
            await LancarErroAsync(resposta, ct);
    }

    /// <summary><paramref name="mediaId"/> é o caminho do arquivo no WAHA (<c>api/files/...</c>), gravado pelo webhook.</summary>
    public async Task<(Stream Conteudo, string MimeType)> BaixarMidiaAsync(string mediaId, CancellationToken ct = default)
    {
        if (!mediaId.StartsWith("api/files/", StringComparison.Ordinal) || mediaId.Contains("..", StringComparison.Ordinal))
            throw new WhatsAppCloudException(0, "Mídia da ponte com caminho inválido.", ehPermanente: true);

        var pipeline = pipelines.GetPipeline(IntegrationCategories.WhatsApp);
        var resposta = await pipeline.ExecuteAsync(
            async t => await http.GetAsync(mediaId, HttpCompletionOption.ResponseHeadersRead, t), ct);
        if (!resposta.IsSuccessStatusCode)
        {
            using (resposta) await LancarErroAsync(resposta, ct);
        }

        var mime = resposta.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return (await resposta.Content.ReadAsStreamAsync(ct), mime);
    }

    /// <summary>#2155: <c>GET api/contacts/profile-picture</c> devolve o endereço da foto no CDN, ou nulo sem foto visível.</summary>
    public async Task<FotoPerfilWhatsApp?> ObterFotoPerfilAsync(string waId, CancellationToken ct = default)
    {
        if (fotos is null) return null;
        var conversa = await ConversaDaEmpresaAsync(waId, ct);
        using var resposta = await http.GetAsync(
            $"api/contacts/profile-picture?contactId={Uri.EscapeDataString(conversa)}&session={Uri.EscapeDataString(_opcoes.Sessao)}", ct);
        if (!resposta.IsSuccessStatusCode)
        {
            logger.LogWarning("Ponte WAHA: a foto do perfil respondeu HTTP {Status}.", (int)resposta.StatusCode);
            return null;
        }

        using var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
        var url = corpo.RootElement.ValueKind == JsonValueKind.Object
            && corpo.RootElement.TryGetProperty("profilePictureURL", out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString() : null;
        return await fotos.BaixarAsync(url, ct);
    }

    private async Task<string> ConversaDaEmpresaAsync(string waId, CancellationToken ct)
    {
        await GarantirEmpresaDaPonteAsync(ct);
        return await resolvedor.ConversaAsync(waId, ct);
    }

    /// <summary>
    /// A ponte é um número só. Sem empresa corrente, ou com empresa vinculada a outro número, nada sai: o cliente de
    /// uma empresa nunca recebe mensagem pelo número de outra (#1292).
    /// </summary>
    private async Task GarantirEmpresaDaPonteAsync(CancellationToken ct)
    {
        var numeroDaPonte = _opcoes.PhoneNumberIdNormalizado;
        var daEmpresa = remetente.HaTenantCorrente ? (await remetente.ObterPhoneNumberIdAsync(ct))?.Trim() : null;
        if (numeroDaPonte.Length > 0 && string.Equals(daEmpresa, numeroDaPonte, StringComparison.Ordinal))
            return;

        throw new WhatsAppCloudException(0,
            "Esta empresa não está vinculada ao número da ponte do WhatsApp: vincule o número da loja (só dígitos) " +
            "em Admin > Tenants > WhatsApp e confira Atendimento:WhatsApp:Waha:PhoneNumberId.",
            ehPermanente: true);
    }

    private async Task<EnvioWhatsAppResult> EnviarAsync(string rota, string conversa, int caracteres, object corpo, CancellationToken ct)
    {
        await UmEnvioPorVez.WaitAsync(ct);
        try
        {
            await DigitarAsync(conversa, caracteres, ct);

            var pipeline = pipelines.GetPipeline(IntegrationCategories.WahaEnvio);
            HttpResponseMessage resposta;
            try
            {
                resposta = await pipeline.ExecuteAsync(
                    async t => await http.PostAsync(rota, JsonContent.Create(corpo, options: Json), t), ct);
            }
            catch (TimeoutRejectedException ex)
            {
                throw new TimeoutException("O WAHA não respondeu ao envio a tempo; a mensagem pode ter saído.", ex);
            }

            using (resposta)
            {
                if (!resposta.IsSuccessStatusCode)
                    await LancarErroAsync(resposta, ct);

                var id = await LerIdAsync(resposta, ct);
                if (string.IsNullOrEmpty(id))
                    throw new WhatsAppCloudException(0, "O WAHA aceitou o envio sem devolver o id da mensagem.",
                        ehPermanente: true, (int)resposta.StatusCode);
                return new EnvioWhatsAppResult(id);
            }
        }
        finally
        {
            UmEnvioPorVez.Release();
        }
    }

    /// <summary>"Digitando" e pausa de gente antes do envio. Falha do "digitando" não impede a mensagem.</summary>
    private async Task DigitarAsync(string conversa, int caracteres, CancellationToken ct)
    {
        var minimo = Math.Max(0, _opcoes.PausaMinimaMs);
        var maximo = Math.Max(minimo, _opcoes.PausaMaximaMs);
        var pausa = Math.Clamp(minimo + (long)caracteres * Math.Max(0, _opcoes.PausaPorCaractereMs), minimo, maximo);
        if (pausa <= 0) return;

        // Com a sessão fora do ar o WAHA segura a chamada ~10 s esperando reconectar; o "digitando" não vale essa espera.
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TempoMaximoDigitando);
        try
        {
            using var digitando = await http.PostAsync("api/startTyping",
                JsonContent.Create(new { session = _opcoes.Sessao, chatId = conversa }, options: Json), limite.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Ponte WAHA: \"digitando\" não saiu; o envio segue.");
        }

        var comVariacao = pausa * (0.85 + Random.Shared.NextDouble() * 0.3);
        await Task.Delay(TimeSpan.FromMilliseconds(comVariacao), relogio, ct);
    }

    private static async Task<string?> LerIdAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await resposta.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("id", out var id))
                return null;
            return id.ValueKind switch
            {
                JsonValueKind.String => id.GetString(),
                JsonValueKind.Object when id.TryGetProperty("_serialized", out var s) && s.ValueKind == JsonValueKind.String => s.GetString(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task LancarErroAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        var status = (int)resposta.StatusCode;
        string detalhe;
        try
        {
            detalhe = await resposta.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            detalhe = string.Empty;
        }

        if (detalhe.Length > 300) detalhe = detalhe[..300];
        logger.LogWarning("Ponte WAHA recusou a chamada: HTTP {Status} {Detalhe}", status, detalhe.ReplaceLineEndings(" "));

        // 400 e chave errada não passam numa nova tentativa; sessão fora do ar (404, 422), limite e timeout passam.
        var permanente = status is 400 or 401 or 403 or 413 or 415;
        throw new WhatsAppCloudException(0, $"WAHA respondeu HTTP {status}.", permanente, status);
    }

    // A mesma regra da Meta (#1474): o WAHA busca a mídia pela URL, então ela precisa ser absoluta.
    private static void ExigirUrlAbsoluta(string urlPublica)
    {
        if (Uri.TryCreate(urlPublica, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            return;

        throw new ArgumentException(
            $"A mídia precisa de URL pública absoluta (http/https), mas veio \"{urlPublica}\". " +
            "Configure FileStorage__PublicBaseUrl com o endereço público da Api.",
            nameof(urlPublica));
    }

    private static string Extensao(string url) =>
        Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();

    private static string NomeDoArquivo(string url)
    {
        var nome = Path.GetFileName(new Uri(url).AbsolutePath);
        return string.IsNullOrWhiteSpace(nome) ? "imagem.jpg" : nome;
    }

    private static string MimeDaImagem(string url) => Extensao(url) switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };

    private static string MimeDoAudio(string url) => Extensao(url) switch
    {
        ".ogg" or ".opus" => "audio/ogg",
        ".mp3" or ".mpeg" => "audio/mpeg",
        ".m4a" or ".mp4" => "audio/mp4",
        ".aac" => "audio/aac",
        ".amr" => "audio/amr",
        ".wav" => "audio/wav",
        _ => "audio/mpeg",
    };
}
