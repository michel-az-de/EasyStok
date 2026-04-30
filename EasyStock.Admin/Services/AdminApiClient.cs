using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EasyStock.Admin.Services;

public class AdminApiClient(HttpClient httpClient, AdminSessionService session, IHttpContextAccessor httpContextAccessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private void AttachToken()
    {
        var token = session.GetToken();
        if (!string.IsNullOrEmpty(token))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static T UnwrapData<T>(JsonElement root)
    {
        var data = root.GetProperty("data");
        return data.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException("API response data was null.");
    }

    private async Task HandleUnauthorized(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            session.ClearSession();
            var ctx = httpContextAccessor.HttpContext;
            if (ctx is not null)
                ctx.Response.Redirect("/Auth/Login");
        }
    }

    public async Task<T> GetAsync<T>(string path)
    {
        AttachToken();
        var response = await httpClient.GetAsync(path);
        await HandleUnauthorized(response);
        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    public async Task<JsonElement> GetRawAsync(string path)
    {
        AttachToken();
        var response = await httpClient.GetAsync(path);
        await HandleUnauthorized(response);
        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public async Task<T> PostAsync<T>(string path, object body)
    {
        AttachToken();
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(path, content);
        await HandleUnauthorized(response);
        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    public async Task<JsonElement> PostRawAsync(string path, object body)
    {
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(path, content);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    public async Task<T> PatchAsync<T>(string path, object body)
    {
        AttachToken();
        var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var response = await httpClient.SendAsync(request);
        await HandleUnauthorized(response);
        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    public async Task DeleteAsync(string path)
    {
        AttachToken();
        var response = await httpClient.DeleteAsync(path);
        await HandleUnauthorized(response);
        response.EnsureSuccessStatusCode();
    }
}
