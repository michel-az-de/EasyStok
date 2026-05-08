using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Typed HttpClient para a API REST do Focus NFe. Auth Basic via token
/// per-tenant (passado em cada call). Resilience aplicada externamente
/// via ResiliencePipelineProvider categoria "fiscal".
/// </summary>
internal sealed class FocusNFeHttpClient(
    HttpClient http,
    IOptions<FocusNFeOptions> options,
    ILogger<FocusNFeHttpClient> log)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly FocusNFeOptions _opt = options.Value;

    public async Task<FocusNFeEmissaoResponse> EmitirAsync(
        FocusNFeEmissaoRequest req,
        Guid notaFiscalId,
        string token,
        AmbienteSefaz ambiente,
        CancellationToken ct)
    {
        var url = $"{_opt.ResolverBaseUrl(ambiente)}/v2/nfce?ref={notaFiscalId:N}";
        return await ExecutarAsync(HttpMethod.Post, url, req, token, _opt.EmissaoTimeout, ct);
    }

    public async Task<FocusNFeEmissaoResponse> ConsultarAsync(
        Guid notaFiscalId, string token, AmbienteSefaz ambiente, CancellationToken ct)
    {
        var url = $"{_opt.ResolverBaseUrl(ambiente)}/v2/nfce/{notaFiscalId:N}";
        return await ExecutarAsync<FocusNFeEmissaoResponse>(HttpMethod.Get, url, null, token, _opt.ConsultaTimeout, ct);
    }

    public async Task<FocusNFeCancelamentoResponse> CancelarAsync(
        Guid notaFiscalId, string justificativa, string token, AmbienteSefaz ambiente, CancellationToken ct)
    {
        var url = $"{_opt.ResolverBaseUrl(ambiente)}/v2/nfce/{notaFiscalId:N}";
        var body = new { justificativa };
        return await ExecutarAsync<FocusNFeCancelamentoResponse>(HttpMethod.Delete, url, body, token, _opt.CancelamentoTimeout, ct);
    }

    public async Task<FocusNFeInutilizacaoResponse> InutilizarAsync(
        Guid empresaId,
        int ano,
        string cnpj,
        string serie,
        int numeroInicial,
        int numeroFinal,
        string justificativa,
        string token,
        AmbienteSefaz ambiente,
        CancellationToken ct)
    {
        var url = $"{_opt.ResolverBaseUrl(ambiente)}/v2/nfce/inutilizacao";
        var body = new
        {
            cnpj,
            serie,
            numero_inicial = numeroInicial,
            numero_final = numeroFinal,
            justificativa,
            ano,
        };
        return await ExecutarAsync<FocusNFeInutilizacaoResponse>(HttpMethod.Post, url, body, token, _opt.CancelamentoTimeout, ct);
    }

    private async Task<TResp> ExecutarAsync<TResp>(
        HttpMethod method, string url, object? body, string token, TimeSpan timeout, CancellationToken ct)
        where TResp : class, new()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            using var msg = new HttpRequestMessage(method, url);
            if (body is not null)
                msg.Content = JsonContent.Create(body, options: JsonOpts);
            msg.Headers.Authorization = BasicAuthHeader(token);

            using var resp = await http.SendAsync(msg, HttpCompletionOption.ResponseContentRead, cts.Token);

            if (resp.IsSuccessStatusCode)
            {
                var ok = await resp.Content.ReadFromJsonAsync<TResp>(JsonOpts, ct) ?? new TResp();
                return ok;
            }

            var status = (int)resp.StatusCode;
            var raw = await resp.Content.ReadAsStringAsync(ct);

            if (status is >= 400 and < 500)
            {
                // 4xx é payload inválido — NÃO retry. Devolve TResp com erros pra adapter mapear.
                log.LogWarning("Focus 4xx em {Url}: {Status} {Body}", url, status, Truncar(raw));
                var parsed = TryDeserialize<TResp>(raw);
                return parsed ?? new TResp();
            }

            // 5xx → exception pra Polly retentar (ou disparar contingência depois).
            throw new FocusUnreachableException(
                $"Focus respondeu {status} em {method} {url}. Body: {Truncar(raw)}");
        }
        catch (TaskCanceledException tce) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(tce, "Timeout {Timeout}s em {Method} {Url}", timeout.TotalSeconds, method, url);
            throw new FocusUnreachableException($"Timeout em {method} {url}.", tce);
        }
        catch (HttpRequestException hre)
        {
            log.LogWarning(hre, "Network error em {Method} {Url}", method, url);
            throw new FocusUnreachableException($"Network error em {method} {url}.", hre);
        }
    }

    // Overload pra Emitir devolver erro estruturado em 4xx.
    private async Task<FocusNFeEmissaoResponse> ExecutarAsync(
        HttpMethod method, string url, object body, string token, TimeSpan timeout, CancellationToken ct)
    {
        return await ExecutarAsync<FocusNFeEmissaoResponse>(method, url, body, token, timeout, ct);
    }

    private static AuthenticationHeaderValue BasicAuthHeader(string token)
    {
        var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{token}:"));
        return new AuthenticationHeaderValue("Basic", creds);
    }

    private static TResp? TryDeserialize<TResp>(string raw) where TResp : class
    {
        try { return JsonSerializer.Deserialize<TResp>(raw, JsonOpts); }
        catch { return null; }
    }

    private static string Truncar(string s, int max = 500) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}
