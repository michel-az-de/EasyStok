using System.Text;
using System.Text.Json;

namespace EasyStock.Admin.Services;

public sealed class SessionExpiredException() : Exception("Sessão expirada. Faça login novamente.");

/// <summary>
/// Cliente HTTP para a API do EasyStock. O Bearer token e o retry com refresh
/// sao injetados pelo <see cref="AdminTokenRefreshHandler"/> no pipeline do
/// HttpClient — esta classe so monta a request e desserializa a resposta.
/// </summary>
public class AdminApiClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }

    private static void ThrowIfUnauthorized(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new SessionExpiredException();
    }

    private static T UnwrapData<T>(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
            throw new InvalidOperationException("API response sem campo 'data'.");
        return data.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException("API response data foi null.");
    }

    public async Task<T> GetAsync<T>(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Get, path));
        ThrowIfUnauthorized(response);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    public async Task<JsonElement> GetRawAsync(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Get, path));
        ThrowIfUnauthorized(response);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public async Task<T> PostAsync<T>(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Post, path, body));
        ThrowIfUnauthorized(response);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    /// <summary>
    /// Mantém o envelope cru pra o caller inspecionar `error`/`data` (usado pelo
    /// Login que precisa diferenciar credenciais inválidas de outros erros).
    /// Não faz EnsureSuccessStatusCode; apenas diferencia 401 (sessão expirada).
    /// Caller é responsável por checar `error` no payload retornado.
    /// </summary>
    public async Task<JsonElement> PostRawAsync(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Post, path, body));
        ThrowIfUnauthorized(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            // Sem corpo: gera envelope sintético com `error` para o caller tratar.
            var fallback = $"{{\"error\":{{\"code\":\"EMPTY_RESPONSE\",\"message\":\"HTTP {(int)response.StatusCode} sem corpo.\"}}}}";
            using var fb = JsonDocument.Parse(fallback);
            return fb.RootElement.Clone();
        }
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public async Task<T> PatchAsync<T>(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Patch, path, body));
        ThrowIfUnauthorized(response);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    public async Task DeleteAsync(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Delete, path));
        ThrowIfUnauthorized(response);
        response.EnsureSuccessStatusCode();
    }

    public async Task<(byte[] Bytes, string ContentType)> GetBytesAsync(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Get, path));
        ThrowIfUnauthorized(response);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var ct = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        return (bytes, ct);
    }
}
