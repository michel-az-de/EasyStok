using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Integrations.Resilience;
using EasyStock.Infra.Integrations.WhatsApp.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Timeout;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Cliente real da Cloud API da Meta. Erros de aplicação (código numérico da Meta, ex.: 131047)
/// nunca passam pelo pipeline Polly como exceção — só a chamada HTTP em si é envolvida por ele,
/// então falha permanente da Meta não é reenviada automaticamente. O <c>POST /messages</c> usa o
/// pipeline <see cref="IntegrationCategories.WhatsAppEnvio"/>, sem retry (#1292): não é idempotente
/// e um timeout depois de a Meta aceitar duplicaria a mensagem. Só o GET de mídia repete.
/// </summary>
public sealed class WhatsAppCloudClient(
    HttpClient httpClient,
    IOptions<WhatsAppCloudOptions> options,
    IRemetenteWhatsApp remetente,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<WhatsAppCloudClient> logger) : IWhatsAppCloudClient, IClienteWhatsAppPlataforma
{
    private readonly WhatsAppCloudOptions _options = options.Value;

    public Task<EnvioWhatsAppResult> EnviarTextoAsync(
        string waId, string texto, string? responderAWamid = null, CancellationToken ct = default)
    {
        object payload = responderAWamid is null
            ? new { messaging_product = "whatsapp", to = waId, type = "text", text = new { body = texto } }
            : new
            {
                messaging_product = "whatsapp",
                to = waId,
                type = "text",
                text = new { body = texto },
                context = new { message_id = responderAWamid }
            };

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    public Task<EnvioWhatsAppResult> EnviarImagemAsync(
        string waId, string urlPublica, string? legenda = null, CancellationToken ct = default)
    {
        ExigirUrlAbsoluta(urlPublica);
        var payload = new
        {
            messaging_product = "whatsapp",
            to = waId,
            type = "image",
            image = new { link = urlPublica, caption = legenda }
        };

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    public Task<EnvioWhatsAppResult> EnviarAudioAsync(
        string waId, string urlPublica, bool notaDeVoz, CancellationToken ct = default)
    {
        ExigirUrlAbsoluta(urlPublica);
        var payload = new
        {
            messaging_product = "whatsapp",
            to = waId,
            type = "audio",
            audio = new { link = urlPublica, voice = notaDeVoz }
        };

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    // #1474: a Meta só busca mídia por URL absoluta http(s). Sem FileStorage__PublicBaseUrl o storage devolve
    // "/files/..." e a Meta recusa; falha antes do POST, com ArgumentException (classificada como permanente).
    private static void ExigirUrlAbsoluta(string urlPublica)
    {
        if (Uri.TryCreate(urlPublica, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            return;

        throw new ArgumentException(
            $"A mídia precisa de URL pública absoluta (http/https) para a Meta buscar, mas veio \"{urlPublica}\". " +
            "Configure FileStorage__PublicBaseUrl com o endereço público da Api (ex.: https://api.exemplo.com/files).",
            nameof(urlPublica));
    }

    public Task<EnvioWhatsAppResult> EnviarBotoesAsync(
        string waId, string corpo, IReadOnlyList<(string Id, string Titulo)> botoes, CancellationToken ct = default)
    {
        if (botoes.Count is 0 or > 3)
            throw new ArgumentException("EnviarBotoesAsync aceita de 1 a 3 botões.", nameof(botoes));

        foreach (var (id, titulo) in botoes)
        {
            if (titulo.Length > 20)
                throw new ArgumentException($"Título de botão excede 20 caracteres: \"{titulo}\".", nameof(botoes));
            if (id.Length > 256)
                throw new ArgumentException($"Id de botão excede 256 caracteres: \"{id}\".", nameof(botoes));
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = waId,
            type = "interactive",
            interactive = new
            {
                type = "button",
                body = new { text = corpo },
                action = new
                {
                    buttons = botoes.Select(b => new { type = "reply", reply = new { id = b.Id, title = b.Titulo } }).ToArray()
                }
            }
        };

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    public Task<EnvioWhatsAppResult> EnviarTemplateAsync(
        string waId,
        string nomeTemplate,
        string idioma,
        IReadOnlyList<string> parametrosCorpo,
        IReadOnlyList<(string Id, string Titulo)>? botoesQuickReply = null,
        string? imagemCabecalho = null,
        CancellationToken ct = default)
    {
        var payload = MetaTemplatePayload.Montar(
            waId, nomeTemplate, idioma, parametrosCorpo, botoesQuickReply, imagemCabecalho);

        return EnviarEExtrairWamidAsync(payload, ct);
    }

    // ===== N6: número de plataforma =====

    public async Task<ResultadoEnvioPlataforma> EnviarTemplatePlataformaAsync(
        EnvioTemplatePlataforma envio, CancellationToken ct = default)
    {
        if (envio.CopyCode && ExcedeCopyCode(envio))
            return new ResultadoEnvioPlataforma(DesfechoEnvio.FalhaPermanente, Erro: "codigo_acima_de_15_caracteres");

        var payload = MetaTemplatePayload.Montar(
            envio.Para, envio.Nome, envio.Idioma, envio.ParametrosCorpo,
            botaoUrl0: envio.BotaoUrl0, botaoUrl1: envio.BotaoUrl1, opacoCallback: envio.OpacoCallback);
        return await EnviarPlataformaAsync(payload, ct);
    }

    public Task<ResultadoEnvioPlataforma> EnviarTextoPlataformaAsync(string waId, string texto, CancellationToken ct = default) =>
        EnviarPlataformaAsync(
            new { messaging_product = "whatsapp", to = waId, type = "text", text = new { body = texto } }, ct);

    private static bool ExcedeCopyCode(EnvioTemplatePlataforma envio) =>
        envio.ParametrosCorpo.Any(p => p.Length > MetaTemplatePayload.CopyCodeTamanhoMaximo)
        || (envio.BotaoUrl0?.Length ?? 0) > MetaTemplatePayload.CopyCodeTamanhoMaximo;

    /// <summary>
    /// POST pelo número de plataforma, sem lançar: no máximo uma vez (pipeline sem retry) e com o desfecho tipado.
    /// Sem resposta (timeout, queda depois do envio) e 5xx são <c>Indeterminado</c>; só o que prova que nada saiu
    /// (resolução de nome, conexão, TLS, disjuntor aberto) é transitório. O cancelamento do chamador sobe.
    /// </summary>
    private async Task<ResultadoEnvioPlataforma> EnviarPlataformaAsync(object payload, CancellationToken ct)
    {
        var phoneNumberId = _options.PhoneNumberIdPlataforma?.Trim();
        if (string.IsNullOrEmpty(phoneNumberId))
            return new ResultadoEnvioPlataforma(DesfechoEnvio.FalhaPermanente,
                Erro: "Notifications:WhatsApp:Plataforma:PhoneNumberId nao configurado");

        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.WhatsAppEnvio);
        var url = $"{phoneNumberId}/messages";
        HttpResponseMessage response;
        try
        {
            response = await pipeline.ExecuteAsync(async pollyCt => await httpClient.PostAsJsonAsync(url, payload, pollyCt), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (BrokenCircuitException ex)
        {
            return new ResultadoEnvioPlataforma(DesfechoEnvio.FalhaTransitoria, Erro: ex.GetType().Name);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError is
            HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError)
        {
            return new ResultadoEnvioPlataforma(DesfechoEnvio.FalhaTransitoria, Erro: ex.HttpRequestError.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or TimeoutException
            or IOException or OperationCanceledException)
        {
            // A Meta pode ter recebido: nunca reenviar sozinho.
            logger.LogError(ex, "WhatsApp de plataforma sem confirmação: entrega indeterminada.");
            return new ResultadoEnvioPlataforma(DesfechoEnvio.Indeterminado, Erro: ex.GetType().Name);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                var wamid = await LerWamidAsync(response, ct);
                return string.IsNullOrEmpty(wamid)
                    ? new ResultadoEnvioPlataforma(DesfechoEnvio.Indeterminado, StatusHttp: status, Erro: "2xx_sem_message_id")
                    : new ResultadoEnvioPlataforma(DesfechoEnvio.Enviado, wamid, StatusHttp: status);
            }

            if (status >= 500)
                return new ResultadoEnvioPlataforma(DesfechoEnvio.Indeterminado, StatusHttp: status, Erro: $"http_{status}");

            var erro = await LerErroAsync(response, ct);
            if (erro?.Code is { } codigo)
            {
                var classe = CodigosErroMeta.Classificar(codigo);
                return new ResultadoEnvioPlataforma(
                    classe == ClasseErroMeta.Transitorio ? DesfechoEnvio.FalhaTransitoria : DesfechoEnvio.FalhaPermanente,
                    CodigoMeta: codigo, Classe: classe, StatusHttp: status, Erro: erro.Message);
            }

            // Sem código da Meta: vale a regra HTTP (4xx exceto 408 e 429 é recusa permanente).
            var permanente = status is >= 400 and < 500 and not (408 or 429);
            return new ResultadoEnvioPlataforma(
                permanente ? DesfechoEnvio.FalhaPermanente : DesfechoEnvio.FalhaTransitoria,
                Classe: permanente ? ClasseErroMeta.Permanente : ClasseErroMeta.Transitorio,
                StatusHttp: status, Erro: $"http_{status}");
        }
    }

    private static async Task<string?> LerWamidAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<MetaSendMessageResponse>(cancellationToken: ct);
            return body?.Messages?.FirstOrDefault()?.Id;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task<MetaError?> LerErroAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<MetaErrorEnvelope>(cancellationToken: ct))?.Error;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task MarcarComoLidaAsync(string wamid, CancellationToken ct = default)
    {
        var payload = new { messaging_product = "whatsapp", status = "read", message_id = wamid };
        using var response = await PostMessagesAsync(payload, ct);
        if (!response.IsSuccessStatusCode)
            await LancarErroAsync(response, ct);
    }

    public async Task<(Stream Conteudo, string MimeType)> BaixarMidiaAsync(string mediaId, CancellationToken ct = default)
    {
        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.WhatsApp);
        var token = await remetente.ObterAccessTokenAsync(ct); // #1417: a mídia do número da loja só abre com o token dela

        using var metadataResponse = await pipeline.ExecuteAsync(
            async pollyCt => await GetComTokenAsync(mediaId, token, pollyCt), ct);
        if (!metadataResponse.IsSuccessStatusCode)
            await LancarErroAsync(metadataResponse, ct);

        var metadata = await metadataResponse.Content.ReadFromJsonAsync<MetaMediaMetadata>(cancellationToken: ct)
            ?? throw new WhatsAppCloudException(0, "Meta não devolveu metadados da mídia.", ehPermanente: true);

        var binarioResponse = await pipeline.ExecuteAsync(
            async pollyCt => await GetComTokenAsync(metadata.Url, token, pollyCt), ct);
        if (!binarioResponse.IsSuccessStatusCode)
            await LancarErroAsync(binarioResponse, ct);

        var conteudo = await binarioResponse.Content.ReadAsStreamAsync(ct);
        return (conteudo, metadata.MimeType);
    }

    private async Task<HttpResponseMessage> GetComTokenAsync(string url, string? token, CancellationToken ct)
    {
        using var request = ComTokenDaEmpresa(HttpMethod.Get, url, token);
        return await httpClient.SendAsync(request, ct);
    }

    private async Task<EnvioWhatsAppResult> EnviarEExtrairWamidAsync(object payload, CancellationToken ct)
    {
        using var response = await PostMessagesAsync(payload, ct);
        if (!response.IsSuccessStatusCode)
            await LancarErroAsync(response, ct);

        var body = await response.Content.ReadFromJsonAsync<MetaSendMessageResponse>(cancellationToken: ct);
        var wamid = body?.Messages?.FirstOrDefault()?.Id;
        if (string.IsNullOrEmpty(wamid))
            throw new WhatsAppCloudException(0, "Meta retornou 2xx sem message id.", ehPermanente: true);

        return new EnvioWhatsAppResult(wamid);
    }

    private async Task<HttpResponseMessage> PostMessagesAsync(object payload, CancellationToken ct)
    {
        var phoneNumberId = await ResolverPhoneNumberIdAsync(ct);
        var token = await remetente.ObterAccessTokenAsync(ct);
        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.WhatsAppEnvio);
        var url = $"{phoneNumberId}/messages";
        return await pipeline.ExecuteAsync(async pollyCt =>
        {
            using var request = ComTokenDaEmpresa(HttpMethod.Post, url, token);
            request.Content = JsonContent.Create(payload);
            return await httpClient.SendAsync(request, pollyCt);
        }, ct);
    }

    /// <summary>
    /// #1417: com business token da empresa (coexistência) ele vai no cabeçalho da requisição e vence o Bearer global
    /// do HttpClient; sem ele a requisição sai como antes, com o global. Mensagem nova a cada tentativa do pipeline.
    /// </summary>
    private static HttpRequestMessage ComTokenDaEmpresa(HttpMethod metodo, string url, string? token)
    {
        var request = new HttpRequestMessage(metodo, url);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// Número da empresa do tenant corrente (#1102). O global de
    /// <c>Notifications:WhatsApp:Meta:PhoneNumberId</c> só vale sem tenant (diagnóstico): empresa sem
    /// número vinculado não fala com o cliente dela pelo número de outra (#1292). Sem número a chamada
    /// é recusada aqui, antes da rede: um POST em "/messages" só devolveria um erro opaco da Meta.
    /// </summary>
    private async Task<string> ResolverPhoneNumberIdAsync(CancellationToken ct)
    {
        var doTenant = await remetente.ObterPhoneNumberIdAsync(ct);
        if (!string.IsNullOrWhiteSpace(doTenant))
            return doTenant.Trim();

        if (remetente.HaTenantCorrente)
            throw new WhatsAppCloudException(0,
                "A empresa não tem phone_number_id do WhatsApp vinculado: vincule o número à empresa " +
                "(Admin > Tenants > WhatsApp). O número global não é usado para cliente de empresa.",
                ehPermanente: true);

        if (!string.IsNullOrWhiteSpace(_options.PhoneNumberId))
            return _options.PhoneNumberId.Trim();

        throw new WhatsAppCloudException(0,
            "Nenhum phone_number_id do WhatsApp configurado: vincule o número à empresa (Admin > Tenants > WhatsApp) " +
            "ou defina Notifications:WhatsApp:Meta:PhoneNumberId.",
            ehPermanente: true);
    }

    private async Task LancarErroAsync(HttpResponseMessage response, CancellationToken ct)
    {
        MetaErrorEnvelope? envelope = null;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<MetaErrorEnvelope>(cancellationToken: ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            // Corpo de erro não veio no formato esperado — segue com código 0 abaixo.
        }

        var erro = envelope?.Error;
        var codigo = erro?.Code ?? 0;
        var mensagem = erro?.Message ?? $"Meta WhatsApp retornou HTTP {(int)response.StatusCode}.";
        logger.LogWarning("Falha WhatsApp Cloud API: codigo={Codigo} mensagem={Mensagem}", codigo, mensagem);
        // O status HTTP segue na exceção (N2): o provider do outbox trata o 5xx como Indeterminado. A permanência vem da
        // tabela única de códigos (N6); sem código da Meta no corpo não há o que classificar e a exceção segue transitória.
        var permanente = erro?.Code is { } c && CodigosErroMeta.EhPermanente(c);
        throw new WhatsAppCloudException(codigo, mensagem, permanente, (int)response.StatusCode);
    }
}
