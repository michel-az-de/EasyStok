using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EasyStock.Admin.Services;

public sealed class SessionExpiredException() : Exception("Sessão expirada. Faça login novamente.");

public class AdminApiClient(HttpClient httpClient, AdminSessionService session)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        var token = session.GetToken();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    public async Task<JsonElement> PostRawAsync(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Post, path, body));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
