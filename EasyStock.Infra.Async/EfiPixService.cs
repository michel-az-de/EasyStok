using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async;

public sealed class EfiPixService(
    HttpClient http,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<EfiPixService> logger) : IEfiPixService
{
    private const string TokenCacheKey = "efi:access_token";

    private string ClientId => configuration["Efi:ClientId"] ?? string.Empty;
    private string ClientSecret => configuration["Efi:ClientSecret"] ?? string.Empty;
    private string ChavePix => configuration["Efi:ChavePix"] ?? string.Empty;

    public async Task<EfiCobrancaStatusResult> ConsultarCobrancaAsync(string txid, CancellationToken ct = default)
    {
        var (response, content) = await GetCobrancaAsync(txid, forceTokenRefresh: false, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            cache.Remove(TokenCacheKey);
            response.Dispose();
            (response, content) = await GetCobrancaAsync(txid, forceTokenRefresh: true, ct);
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            return new EfiCobrancaStatusResult(txid, EfiCobrancaStatus.Desconhecido);
        }
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Efi consulta {Txid} retornou {Status}: {Body}", txid, (int)response.StatusCode, content);
            return new EfiCobrancaStatusResult(txid, EfiCobrancaStatus.Desconhecido);
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var statusStr = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
        var status = MapStatus(statusStr);

        decimal? valorOriginal = null;
        if (root.TryGetProperty("valor", out var vEl) && vEl.TryGetProperty("original", out var voEl)
            && decimal.TryParse(voEl.GetString(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var vo))
            valorOriginal = vo;

        decimal? valorPago = null;
        DateTime? pagoEm = null;
        string? e2eId = null;
        if (root.TryGetProperty("pix", out var pixArr) && pixArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in pixArr.EnumerateArray())
            {
                if (p.TryGetProperty("valor", out var vp)
                    && decimal.TryParse(vp.GetString(), System.Globalization.NumberStyles.Number,
                        System.Globalization.CultureInfo.InvariantCulture, out var vpDec))
                    valorPago = (valorPago ?? 0m) + vpDec;
                if (e2eId is null && p.TryGetProperty("endToEndId", out var e2)) e2eId = e2.GetString();
                if (pagoEm is null && p.TryGetProperty("horario", out var ho)
                    && DateTime.TryParse(ho.GetString(), out var ts)) pagoEm = ts;
            }
        }

        return new EfiCobrancaStatusResult(txid, status, valorOriginal, valorPago, null, pagoEm, e2eId);
    }

    private async Task<(HttpResponseMessage Response, string Content)> GetCobrancaAsync(
        string txid, bool forceTokenRefresh, CancellationToken ct)
    {
        if (forceTokenRefresh) cache.Remove(TokenCacheKey);
        var token = await ObterTokenAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/v2/cob/{txid}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return (response, content);
    }

    private static EfiCobrancaStatus MapStatus(string raw) => raw?.Trim().ToUpperInvariant() switch
    {
        "ATIVA" => EfiCobrancaStatus.Ativa,
        "CONCLUIDA" => EfiCobrancaStatus.Concluida,
        "REMOVIDA_PELO_USUARIO_RECEBEDOR" => EfiCobrancaStatus.RemovidaPeloUsuario,
        "REMOVIDA_PELO_PSP" => EfiCobrancaStatus.RemovidaPeloPsp,
        _ => EfiCobrancaStatus.Desconhecido
    };

    public async Task<EfiEstornoResult> EstornarAsync(string e2eId, string idSolicitacao, decimal valor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(e2eId))
            return new EfiEstornoResult(false, Mensagem: "e2eId obrigatorio para estorno Pix.");
        if (string.IsNullOrWhiteSpace(idSolicitacao))
            idSolicitacao = Guid.NewGuid().ToString("N")[..32]; // Efi exige <=35 chars

        var token = await ObterTokenAsync(ct);

        var body = new
        {
            valor = valor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/v2/pix/{e2eId}/devolucao/{idSolicitacao}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            cache.Remove(TokenCacheKey);
            response.Dispose();
            token = await ObterTokenAsync(ct);
            request = new HttpRequestMessage(HttpMethod.Put, $"/v2/pix/{e2eId}/devolucao/{idSolicitacao}")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            response = await http.SendAsync(request, ct);
            content = await response.Content.ReadAsStringAsync(ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Efi estorno {E2eId} retornou {Status}: {Body}", e2eId, (int)response.StatusCode, content);
            return new EfiEstornoResult(false, Mensagem: $"HTTP {(int)response.StatusCode}: {content}");
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : idSolicitacao;
            var status = root.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
            return new EfiEstornoResult(true, id, status, "Estorno solicitado com sucesso.");
        }
        catch
        {
            return new EfiEstornoResult(true, idSolicitacao, "DEVOLVIDO", "Estorno aceito (resposta sem JSON).");
        }
    }

    public async Task<EfiCobrancaResult> CriarCobrancaAsync(
        string txid,
        decimal valor,
        string descricao,
        CancellationToken ct = default)
    {
        var (response, content) = await EnviarCobrancaAsync(txid, valor, descricao, forceTokenRefresh: false, ct);

        // Se Efí rotacionou/revogou o token antes do TTL local de 3500s,
        // recebemos 401. Invalida cache e tenta uma vez mais.
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Efí retornou 401 com token cacheado. Invalidando e tentando novamente.");
            cache.Remove(TokenCacheKey);
            response.Dispose();
            (response, content) = await EnviarCobrancaAsync(txid, valor, descricao, forceTokenRefresh: true, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Efí Bank retornou {Status} ao criar cobrança {Txid}: {Body}", (int)response.StatusCode, txid, content);
            throw new InvalidOperationException($"Falha ao criar cobrança Pix: {(int)response.StatusCode} — {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var pixCopiaCola = root.TryGetProperty("pixCopiaECola", out var pcc) ? pcc.GetString() ?? string.Empty : string.Empty;
        var qrCodeBase64 = root.TryGetProperty("imagemQrcode", out var qr) ? qr.GetString() ?? string.Empty : string.Empty;
        var expiracaoEm = DateTime.UtcNow.AddDays(1);

        logger.LogInformation("Cobrança Pix criada. Txid: {Txid}, Valor: {Valor}", txid, valor);

        return new EfiCobrancaResult(txid, pixCopiaCola, qrCodeBase64, expiracaoEm);
    }

    private async Task<(HttpResponseMessage Response, string Content)> EnviarCobrancaAsync(
        string txid, decimal valor, string descricao, bool forceTokenRefresh, CancellationToken ct)
    {
        if (forceTokenRefresh) cache.Remove(TokenCacheKey);
        var token = await ObterTokenAsync(ct);

        var body = new
        {
            calendario = new { expiracao = 86400 },
            valor = new { original = valor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
            chave = ChavePix,
            solicitacaoPagador = $"EasyStock — {descricao}",
            infoAdicionais = new[] { new { nome = "Sistema", valor = "EasyStock" } }
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"/v2/cob/{txid}")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return (response, content);
    }

    private async Task<string> ObterTokenAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null)
            return cached;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token")
        {
            Content = new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Falha ao autenticar na API Efí: {content}");

        using var doc = JsonDocument.Parse(content);
        var token = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token vazio na resposta da API Efí.");

        cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(3500));
        return token;
    }
}

/// <summary>No-op usado quando Efi:ClientId não está configurado.</summary>
public sealed class NoopEfiPixService(ILogger<NoopEfiPixService> logger) : IEfiPixService
{
    public Task<EfiCobrancaResult> CriarCobrancaAsync(string txid, decimal valor, string descricao, CancellationToken ct = default)
    {
        logger.LogWarning("EfiPixService não configurado (Efi:ClientId vazio). Cobrança ignorada para txid {Txid}.", txid);
        return Task.FromException<EfiCobrancaResult>(
            new InvalidOperationException("Gateway Pix não configurado. Defina Efi:ClientId, Efi:ClientSecret e Efi:ChavePix."));
    }

    public Task<EfiCobrancaStatusResult> ConsultarCobrancaAsync(string txid, CancellationToken ct = default)
    {
        logger.LogDebug("NoopEfiPixService: consulta {Txid} → Desconhecido (gateway nao configurado).", txid);
        return Task.FromResult(new EfiCobrancaStatusResult(txid, EfiCobrancaStatus.Desconhecido));
    }

    public Task<EfiEstornoResult> EstornarAsync(string e2eId, string idSolicitacao, decimal valor, CancellationToken ct = default)
    {
        logger.LogWarning("NoopEfiPixService: estorno ignorado para e2eId {E2eId} (gateway nao configurado).", e2eId);
        return Task.FromResult(new EfiEstornoResult(false, Mensagem: "Gateway Pix nao configurado."));
    }
}
