using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async;

/// <summary>
/// Emissão de boleto bancário via Efí Bank API v1.
/// Autenticação reutiliza o mesmo token OAuth2 do Pix (client_credentials).
/// </summary>
public sealed class EfiBoletoService(
    HttpClient http,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<EfiBoletoService> logger) : IEfiBoletoService
{
    private const string TokenCacheKey = "efi:access_token";

    private string ClientId => configuration["Efi:ClientId"] ?? string.Empty;
    private string ClientSecret => configuration["Efi:ClientSecret"] ?? string.Empty;

    public async Task<EfiBoletoResult> CriarBoletoAsync(
        string txid,
        decimal valor,
        string descricao,
        string nomeDevedor,
        string cpfCnpjDevedor,
        CancellationToken ct = default)
    {
        var token = await ObterTokenAsync(ct);

        // Efí Bank: POST /v1/charge cria a cobrança, depois PUT /v1/charge/{id}/billet gera o boleto.
        // Para simplificar: criamos one-shot com billet_split.
        // Documentação: https://dev.efipay.com.br/docs/api-cobrancas/boleto
        var chargeBody = new
        {
            items = new[]
            {
                new
                {
                    name = descricao.Length > 100 ? descricao[..100] : descricao,
                    value = (int)(valor * 100), // Efí espera centavos como inteiro
                    amount = 1
                }
            },
            customer = new
            {
                name = nomeDevedor.Length > 80 ? nomeDevedor[..80] : nomeDevedor,
                cpf = cpfCnpjDevedor.Length <= 11 ? cpfCnpjDevedor : (string?)null,
                cnpj = cpfCnpjDevedor.Length > 11 ? cpfCnpjDevedor : (string?)null,
                email = (string?)null
            },
            expire_at = DateTime.UtcNow.AddDays(5).ToString("yyyy-MM-dd")
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/charge")
        {
            Content = new StringContent(JsonSerializer.Serialize(chargeBody), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Efí Boleto retornou {Status} para txid {Txid}: {Body}", (int)response.StatusCode, txid, content);
            throw new InvalidOperationException($"Falha ao criar boleto: {(int)response.StatusCode} — {content}");
        }

        using var doc = JsonDocument.Parse(content);
        var data = doc.RootElement.GetProperty("data");
        var chargeId = data.GetProperty("charge_id").GetInt64();
        var barcode = data.TryGetProperty("barcode", out var bc) ? bc.GetString() ?? string.Empty : string.Empty;
        var boletoUrl = data.TryGetProperty("link", out var lk) ? lk.GetString() : null;

        logger.LogInformation("Boleto criado. Txid={Txid} ChargeId={ChargeId} Valor={Valor}", txid, chargeId, valor);

        return new EfiBoletoResult(txid, barcode, boletoUrl, DateTime.UtcNow.AddDays(5));
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
            throw new InvalidOperationException($"Falha ao autenticar na API Efí (Boleto): {content}");

        using var doc = JsonDocument.Parse(content);
        var token = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token vazio na resposta da API Efí.");

        cache.Set(TokenCacheKey, token, TimeSpan.FromSeconds(3500));
        return token;
    }
}

public sealed class NoopEfiBoletoService(ILogger<NoopEfiBoletoService> logger) : IEfiBoletoService
{
    public Task<EfiBoletoResult> CriarBoletoAsync(string txid, decimal valor, string descricao,
        string nomeDevedor, string cpfCnpjDevedor, CancellationToken ct = default)
    {
        logger.LogWarning("EfiBoletoService não configurado. Boleto ignorado para txid {Txid}.", txid);
        return Task.FromException<EfiBoletoResult>(
            new InvalidOperationException("Gateway Boleto não configurado."));
    }
}
