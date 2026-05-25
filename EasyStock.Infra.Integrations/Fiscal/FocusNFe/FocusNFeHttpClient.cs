using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;
using EasyStock.Infra.Integrations.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Typed client HTTP do Focus NFe. Wraps todas as chamadas em pipeline Polly
/// registrado em <see cref="IntegrationCategories.Fiscal"/> (retry exponencial +
/// circuit breaker + timeout). Mapeia exceptions HTTP/timeout para
/// <see cref="GatewayFiscalException"/> tipadas.
///
/// <para>
/// <b>Autenticacao:</b> Focus usa Basic Auth com token como username (senha vazia).
/// Token e por tenant — vem do <see cref="ConfigFiscalDto.CredencialToken"/>.
/// </para>
///
/// <para>
/// <b>Idempotencia:</b> Focus aceita <c>ref</c> (idempotency reference) no payload.
/// Reenvio com mesmo <c>ref</c> retorna a mesma autorizacao ja gerada — nao duplica.
/// </para>
/// </summary>
public sealed class FocusNFeHttpClient(
    HttpClient httpClient,
    IOptions<FocusNFeOptions> options,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<FocusNFeHttpClient> logger)
{
    private readonly FocusNFeOptions _options = options.Value;

    public async Task<FocusNFeEmissaoResponse> EmitirAsync(
        string referencia,
        string token,
        FocusNFeEmissaoRequest payload,
        CancellationToken ct = default)
    {
        ConfigurarBaseAuth(token);

        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.Fiscal);
        var url = $"nfce?ref={Uri.EscapeDataString(referencia)}";

        return await pipeline.ExecuteAsync(async (ctx) =>
        {
            using var resp = await httpClient.PostAsJsonAsync(url, payload, ctx);

            if (resp.IsSuccessStatusCode)
            {
                var ok = await resp.Content.ReadFromJsonAsync<FocusNFeEmissaoResponse>(cancellationToken: ctx)
                    ?? throw new GatewayFiscalTransienteException("Focus retornou body vazio em 2xx.");
                return ok;
            }

            await TratarErroAsync(resp, ctx, "EmitirNFCe", referencia);
            throw new InvalidOperationException("Unreachable — TratarErroAsync sempre lanca.");
        }, ct);
    }

    public async Task<FocusNFeEmissaoResponse> ConsultarAsync(
        string referencia,
        string token,
        CancellationToken ct = default)
    {
        ConfigurarBaseAuth(token);

        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.Fiscal);
        var url = $"nfce/{Uri.EscapeDataString(referencia)}";

        return await pipeline.ExecuteAsync(async (ctx) =>
        {
            using var resp = await httpClient.GetAsync(url, ctx);

            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadFromJsonAsync<FocusNFeEmissaoResponse>(cancellationToken: ctx)
                    ?? throw new GatewayFiscalTransienteException("Focus consulta retornou body vazio.");
            }

            await TratarErroAsync(resp, ctx, "ConsultarNFCe", referencia);
            throw new InvalidOperationException("Unreachable.");
        }, ct);
    }

    public async Task<FocusNFeCancelamentoResponse> CancelarAsync(
        string referencia,
        string token,
        string justificativa,
        CancellationToken ct = default)
    {
        ConfigurarBaseAuth(token);

        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.Fiscal);
        var url = $"nfce/{Uri.EscapeDataString(referencia)}";
        var body = new { justificativa };

        return await pipeline.ExecuteAsync(async (ctx) =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = JsonContent.Create(body),
            };
            using var resp = await httpClient.SendAsync(req, ctx);

            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadFromJsonAsync<FocusNFeCancelamentoResponse>(cancellationToken: ctx)
                    ?? throw new GatewayFiscalTransienteException("Focus cancelamento retornou body vazio.");
            }

            await TratarErroAsync(resp, ctx, "CancelarNFCe", referencia);
            throw new InvalidOperationException("Unreachable.");
        }, ct);
    }

    public async Task<FocusNFeInutilizacaoResponse> InutilizarAsync(
        string cnpjEmitente,
        short serie,
        long numeroInicial,
        long numeroFinal,
        string justificativa,
        string token,
        CancellationToken ct = default)
    {
        ConfigurarBaseAuth(token);

        var pipeline = pipelineProvider.GetPipeline(IntegrationCategories.Fiscal);
        var body = new
        {
            cnpj = cnpjEmitente,
            serie,
            numero_inicial = numeroInicial,
            numero_final = numeroFinal,
            justificativa,
        };

        return await pipeline.ExecuteAsync(async (ctx) =>
        {
            using var resp = await httpClient.PostAsJsonAsync("nfce/inutilizacao", body, ctx);

            if (resp.IsSuccessStatusCode)
            {
                return await resp.Content.ReadFromJsonAsync<FocusNFeInutilizacaoResponse>(cancellationToken: ctx)
                    ?? throw new GatewayFiscalTransienteException("Focus inutilizacao retornou body vazio.");
            }

            await TratarErroAsync(resp, ctx, "InutilizarNFCe", $"{cnpjEmitente}/{serie}/{numeroInicial}-{numeroFinal}");
            throw new InvalidOperationException("Unreachable.");
        }, ct);
    }

    private void ConfigurarBaseAuth(string token)
    {
        if (httpClient.BaseAddress is null)
            httpClient.BaseAddress = new Uri(_options.BaseUrl);

        // Focus: Basic Auth com token como usuario e senha vazia
        var raw = $"{token}:";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }

    private async Task TratarErroAsync(HttpResponseMessage resp, CancellationToken ct, string operacao, string referencia)
    {
        var status = (int)resp.StatusCode;
        var body = await SafeReadBodyAsync(resp, ct);

        if (status is 401 or 403)
        {
            logger.LogError("Focus {Op} ref={Ref} {Status}: credencial invalida.", operacao, referencia, status);
            throw new GatewayFiscalCredencialException($"Focus {status}: credencial Focus invalida ou token expirado.");
        }

        if (status >= 500)
        {
            logger.LogWarning("Focus {Op} ref={Ref} {Status} body={Body} — falha transiente.", operacao, referencia, status, body);
            throw new GatewayFiscalTransienteException($"Focus {status}: {body}");
        }

        // 4xx — tentar parsear erro estruturado
        FocusNFeEmissaoResponse? erro = null;
        try
        {
            erro = System.Text.Json.JsonSerializer.Deserialize<FocusNFeEmissaoResponse>(body);
        }
        catch { /* body nao e JSON estruturado */ }

        var motivo = erro?.MensagemSefaz ?? erro?.Mensagem ?? body;
        var codigo = erro?.StatusSefaz ?? erro?.Codigo;

        // SEFAZ codigos 110, 301, 302 = denegada (irregularidade contribuinte)
        if (codigo is "110" or "301" or "302")
            throw new GatewayFiscalDenegadaException(motivo ?? $"Focus {status}");

        logger.LogWarning("Focus {Op} ref={Ref} {Status} codigo={Codigo} motivo={Motivo}", operacao, referencia, status, codigo, motivo);
        throw new GatewayFiscalRejeitadaException(motivo ?? $"Focus {status}", codigo);
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }
}
