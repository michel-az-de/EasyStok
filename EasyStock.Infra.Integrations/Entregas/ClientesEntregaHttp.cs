using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EasyStock.Infra.Integrations.Entregas;

public sealed class UberDirectHttpClient(HttpClient http, string customerId)
{
    private string Caminho => $"v1/customers/{Uri.EscapeDataString(customerId)}/";

    public async Task<UberCotacaoResponse> CotarAsync(UberCotacaoRequest dados, CancellationToken ct = default)
    {
        ValidarEndereco(dados.PickupAddress, dados.PickupLatitude, dados.PickupLongitude);
        ValidarEndereco(dados.DropoffAddress, dados.DropoffLatitude, dados.DropoffLongitude);
        using var resposta = await http.PostAsJsonAsync(Caminho + "delivery_quotes", dados, ct);
        return await LerAsync<UberCotacaoResponse>(resposta, ct);
    }

    public async Task<UberEntregaResponse> CriarAsync(UberEntregaRequest dados, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dados.QuoteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dados.IdempotencyKey);
        if (dados.ManifestItems.Count == 0 || dados.ManifestItems.Any(i => i.Quantity < 1))
            throw new ArgumentException("Informe os itens da entrega.", nameof(dados));
        using var resposta = await http.PostAsJsonAsync(Caminho + "deliveries", dados, ct);
        return await LerAsync<UberEntregaResponse>(resposta, ct);
    }

    public async Task<UberEntregaResponse> ConsultarAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await http.GetAsync(Caminho + "deliveries/" + Uri.EscapeDataString(id), ct);
        return await LerAsync<UberEntregaResponse>(resposta, ct);
    }

    public async Task<UberEntregaResponse> CancelarAsync(string id, CancellationToken ct = default)
    {
        using var resposta = await http.PostAsync(Caminho + "deliveries/" + Uri.EscapeDataString(id) + "/cancel", null, ct);
        return await LerAsync<UberEntregaResponse>(resposta, ct);
    }

    internal static void ValidarEndereco(string endereco, double latitude, double longitude)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endereco);
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new ArgumentException("Coordenadas da entrega inválidas.");
    }

    internal static async Task<T> LerAsync<T>(HttpResponseMessage resposta, CancellationToken ct)
    {
        // Falha externa nunca vira sucesso simulado. Corpo externo não é exposto em mensagens/logs.
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<T>(ct) ?? throw new JsonException("Resposta de entrega vazia.");
    }
}

public sealed class LalamoveHttpClient(HttpClient http, string apiKey, string secret, TimeProvider relogio)
{
    public async Task<LalamoveCotacaoResponse> CotarAsync(LalamoveCotacaoRequest dados, CancellationToken ct = default)
    {
        // A tabela do contrato limita a 16 paradas incluindo a coleta; não usar o texto divergente de erro (17).
        if (dados.Stops.Count is < 2 or > 16)
            throw new ArgumentException("Lalamove aceita de 2 a 16 paradas, incluindo a coleta.", nameof(dados));
        foreach (var parada in dados.Stops)
        {
            if (!double.TryParse(parada.Coordinates.Lat, System.Globalization.CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(parada.Coordinates.Lng, System.Globalization.CultureInfo.InvariantCulture, out var lng))
                throw new ArgumentException("Coordenadas da entrega inválidas.", nameof(dados));
            UberDirectHttpClient.ValidarEndereco(parada.Address, lat, lng);
        }
        if (dados.ScheduleAt < relogio.GetUtcNow())
            throw new ArgumentException("A coleta não pode ser agendada no passado.", nameof(dados));
        return await EnviarAsync<LalamoveCotacaoResponse>(HttpMethod.Post, "/v3/quotations", new LalamoveEnvelope<LalamoveCotacaoRequest>(dados), ct);
    }

    public Task<LalamoveEntregaResponse> CriarAsync(LalamoveEntregaRequest dados, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dados.QuotationId);
        if (dados.Recipients.Count is < 1 or > 15)
            throw new ArgumentException("Informe de 1 a 15 destinatários.", nameof(dados));
        return EnviarAsync<LalamoveEntregaResponse>(HttpMethod.Post, "/v3/orders", new LalamoveEnvelope<LalamoveEntregaRequest>(dados), ct);
    }

    public Task<LalamoveEntregaResponse> ConsultarAsync(string id, CancellationToken ct = default) =>
        EnviarAsync<LalamoveEntregaResponse>(HttpMethod.Get, "/v3/orders/" + Uri.EscapeDataString(id), null, ct);

    public Task<IReadOnlyList<LalamoveCidade>> ListarCidadesAsync(CancellationToken ct = default) =>
        EnviarAsync<IReadOnlyList<LalamoveCidade>>(HttpMethod.Get, "/v3/cities", null, ct);

    public async Task CancelarAsync(string id, CancellationToken ct = default)
    {
        using var request = CriarRequest(HttpMethod.Delete, "/v3/orders/" + Uri.EscapeDataString(id), null);
        using var resposta = await http.SendAsync(request, ct);
        resposta.EnsureSuccessStatusCode();
    }

    private async Task<T> EnviarAsync<T>(HttpMethod metodo, string caminho, object? dados, CancellationToken ct)
    {
        using var request = CriarRequest(metodo, caminho, dados);
        using var resposta = await http.SendAsync(request, ct);
        if (!resposta.IsSuccessStatusCode)
            throw new LalamoveErroException(resposta.StatusCode, PrimeiroErro(await resposta.Content.ReadAsStringAsync(ct)));
        return (await UberDirectHttpClient.LerAsync<LalamoveEnvelope<T>>(resposta, ct)).Data;
    }

    /// <summary>Formato de erro da v3: <c>{"errors":[{"id":"ERR_...","message":"..."}]}</c>. Só o id sai daqui.</summary>
    private static string? PrimeiroErro(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Array
                && erros.GetArrayLength() > 0 && erros[0].ValueKind == JsonValueKind.Object
                && erros[0].TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                return id.GetString();
        }
        catch (JsonException)
        {
            // corpo fora do formato: fica só o status HTTP
        }
        return null;
    }

    private HttpRequestMessage CriarRequest(HttpMethod metodo, string caminho, object? dados)
    {
        var corpo = dados is null ? "" : JsonSerializer.Serialize(dados);
        var timestamp = relogio.GetUtcNow().ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var assinatura = AssinaturasEntrega.Lalamove(secret, timestamp, metodo.Method, caminho, corpo);
        var request = new HttpRequestMessage(metodo, caminho);
        request.Headers.TryAddWithoutValidation("Authorization", $"hmac {apiKey}:{timestamp}:{assinatura}");
        request.Headers.Add("Market", "BR");
        request.Headers.Add("Request-ID", Guid.NewGuid().ToString());
        if (dados is not null) request.Content = new StringContent(corpo, Encoding.UTF8, "application/json");
        return request;
    }
}

/// <summary>Recusa da Lalamove com o status e o id do erro (<c>ERR_OUT_OF_SERVICE_AREA</c>), nunca o corpo.</summary>
public sealed class LalamoveErroException(System.Net.HttpStatusCode status, string? erroId)
    : HttpRequestException($"Lalamove recusou: {erroId ?? "HTTP " + (int)status}.", null, status)
{
    public string? ErroId { get; } = erroId;
}

public static class AssinaturasEntrega
{
    // https://developers.lalamove.com/#signature
    public static string Lalamove(string secret, string timestamp, string metodo, string caminho, string corpo) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}\r\n{metodo}\r\n{caminho}\r\n\r\n{corpo}")));

    // https://developer.uber.com/docs/deliveries/guides/webhooks
    public static bool VerificarWebhookUber(string signingKey, ReadOnlySpan<byte> corpoOriginal, string assinatura)
    {
        if (string.IsNullOrWhiteSpace(signingKey) || assinatura.Length != 64) return false;
        byte[] recebido;
        try { recebido = Convert.FromHexString(assinatura); }
        catch (FormatException) { return false; }
        var esperado = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), corpoOriginal);
        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }
}
