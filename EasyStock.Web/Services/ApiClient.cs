using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

public class ApiClient(HttpClient http, ILogger<ApiClient> log)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<ApiResult<T>> GetAsync<T>(string path)
    {
        try
        {
            var response = await http.GetAsync(path);
            return await ParseResponse<T>(response);
        }
        catch (TaskCanceledException)
        {
            log.LogWarning("GET {Path} timed out", path);
            return ApiResult<T>.Fail("TIMEOUT", "Servidor não respondeu. Verifique sua conexão.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on GET {Path}", path);
            return ApiResult<T>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    public async Task<ApiResult<T>> PostAsync<T>(string path, object body)
    {
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            var response = await http.PostAsync(path, content);
            return await ParseResponse<T>(response);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<T>.Fail("TIMEOUT", "Servidor não respondeu. Verifique sua conexão.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on POST {Path}", path);
            return ApiResult<T>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    public async Task<ApiResult<T>> PutAsync<T>(string path, object body)
    {
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            var response = await http.PutAsync(path, content);
            return await ParseResponse<T>(response);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<T>.Fail("TIMEOUT", "Servidor não respondeu. Verifique sua conexão.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on PUT {Path}", path);
            return ApiResult<T>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    public async Task<ApiResult<T>> PatchAsync<T>(string path, object body)
    {
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            var response = await http.PatchAsync(path, content);
            return await ParseResponse<T>(response);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<T>.Fail("TIMEOUT", "Servidor não respondeu. Verifique sua conexão.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on PATCH {Path}", path);
            return ApiResult<T>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    public async Task<ApiResult<bool>> DeleteAsync(string path)
    {
        try
        {
            var response = await http.DeleteAsync(path);
            if (response.IsSuccessStatusCode) return ApiResult<bool>.Ok(true);
            return await ParseErrorResponse<bool>(response);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<bool>.Fail("TIMEOUT", "Servidor não respondeu. Verifique sua conexão.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on DELETE {Path}", path);
            return ApiResult<bool>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    public async Task<ApiResult<Stream>> GetStreamAsync(string path)
    {
        try
        {
            var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return ApiResult<Stream>.Fail("HTTP_ERROR", $"Erro HTTP {(int)response.StatusCode}.");
            var stream = await response.Content.ReadAsStreamAsync();
            return ApiResult<Stream>.Ok(stream);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<Stream>.Fail("TIMEOUT", "Servidor não respondeu.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on GET stream {Path}", path);
            return ApiResult<Stream>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    public async Task<ApiResult<Stream>> PostStreamAsync(string path, object body)
    {
        try
        {
            var json = JsonSerializer.Serialize(body, JsonOpts);
            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return ApiResult<Stream>.Fail("HTTP_ERROR", $"Erro HTTP {(int)response.StatusCode}.");
            var stream = await response.Content.ReadAsStreamAsync();
            return ApiResult<Stream>.Ok(stream);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<Stream>.Fail("TIMEOUT", "Servidor não respondeu.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on POST stream {Path}", path);
            return ApiResult<Stream>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    public async Task<ApiResult<T>> PostMultipartAsync<T>(string path, MultipartFormDataContent form)
    {
        try
        {
            var response = await http.PostAsync(path, form);
            return await ParseResponse<T>(response);
        }
        catch (TaskCanceledException)
        {
            return ApiResult<T>.Fail("TIMEOUT", "Servidor não respondeu. Verifique sua conexão.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on POST multipart {Path}", path);
            return ApiResult<T>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    private async Task<ApiResult<T>> ParseResponse<T>(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                JsonElement payload = IsPagedResultType(typeof(T))
                    ? root
                    : root.TryGetProperty("data", out var d) ? d : root;

                var data = payload.Deserialize<T>(JsonOpts);
                return data is null
                    ? ApiResult<T>.Fail("EMPTY_RESPONSE", "Resposta vazia do servidor.", status)
                    : ApiResult<T>.Ok(data) with { HttpStatus = status };
            }
            catch (JsonException ex)
            {
                log.LogError(ex, "JSON deserialization failed for {Type}", typeof(T).Name);
                return ApiResult<T>.Fail("PARSE_ERROR", "Erro ao processar resposta do servidor.", status);
            }
        }

        return await ParseErrorResponse<T>(response);
    }

    private async Task<ApiResult<T>> ParseErrorResponse<T>(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                ApiResult<T>.Fail("AUTH_TOKEN_EXPIRED", "Sessão expirada. Faça login novamente.", status),
            HttpStatusCode.PaymentRequired =>
                ApiResult<T>.Fail("LIMITE_PLANO", "Limite do plano atingido.", status),
            HttpStatusCode.Forbidden =>
                ApiResult<T>.Fail("PERMISSAO_INSUFICIENTE", "Você não tem permissão para esta ação.", status),
            HttpStatusCode.NotFound =>
                ApiResult<T>.Fail("NOT_FOUND", "Recurso não encontrado.", status),
            HttpStatusCode.TooManyRequests =>
                ApiResult<T>.Fail("LIMITE_IA", "Cota de IA esgotada.", status),
            HttpStatusCode.InternalServerError =>
                ApiResult<T>.Fail("SERVER_ERROR", "Erro no servidor. Tente novamente.", status),
            HttpStatusCode.Conflict => await ParseBodyError<T>(response, status),
            HttpStatusCode.UnprocessableEntity => await ParseBodyError<T>(response, status),
            _ => ApiResult<T>.Fail("HTTP_ERROR", $"Erro HTTP {status}.", status)
        };
    }

    private async Task<ApiResult<T>> ParseBodyError<T>(HttpResponseMessage response, int status)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try { error: { code, message } } or { code, message }
            JsonElement errEl = root.TryGetProperty("error", out var e) ? e : root;
            var code = errEl.TryGetProperty("code", out var c) ? c.GetString() : "API_ERROR";
            var msg = errEl.TryGetProperty("message", out var m) ? m.GetString() : "Erro na requisição.";

            return ApiResult<T>.Fail(code ?? "API_ERROR", msg ?? "Erro na requisição.", status);
        }
        catch
        {
            return ApiResult<T>.Fail("API_ERROR", "Erro na requisição.", status);
        }
    }
    private static bool IsPagedResultType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PagedResult<>);
}
