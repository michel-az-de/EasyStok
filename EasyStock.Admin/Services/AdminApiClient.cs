using System.Net;
using System.Text;
using System.Text.Json;

namespace EasyStock.Admin.Services;

public sealed class SessionExpiredException() : Exception("Sessão expirada. Faça login novamente.");

/// <summary>
/// Erro de chamada à API que carrega mensagem AMIGÁVEL extraída do envelope JSON
/// quando disponível. Usar <c>ex.Message</c> em toasts é seguro — não vaza
/// stacktrace nem mensagem padrão "Response status code does not indicate success".
/// </summary>
public sealed class ApiException : Exception
{
    public int HttpStatus { get; }
    public string? ErrorCode { get; }

    public ApiException(int httpStatus, string? errorCode, string mensagem)
        : base(mensagem)
    {
        HttpStatus = httpStatus;
        ErrorCode = errorCode;
    }
}

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
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new SessionExpiredException();
    }

    /// <summary>
    /// Substitui <c>EnsureSuccessStatusCode</c> — em vez de jogar HttpRequestException
    /// com mensagem técnica, parseia o corpo JSON e devolve <see cref="ApiException"/>
    /// com mensagem amigável. Suporta os 3 envelopes que a API usa
    /// ({error:{message}}, {errors:{}} validation, {message}). Se o body não tem
    /// nada útil, fallback por status HTTP em português.
    /// </summary>
    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var status = (int)response.StatusCode;
        string? code = null;
        string? msg = null;

        try
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Format 1: { error: { code, message, detail } }
                if (root.TryGetProperty("error", out var errEl))
                {
                    code = TryString(errEl, "code");
                    msg = TryString(errEl, "detail") ?? TryString(errEl, "message");
                }
                // Format 2: { errors: { Field: ["msg", ...] } } — FluentValidation/ASP.NET
                else if (root.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Object)
                {
                    var primeira = errorsEl.EnumerateObject().FirstOrDefault();
                    if (primeira.Value.ValueKind == JsonValueKind.Array)
                    {
                        var arr = primeira.Value.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                        msg = arr.Count > 0 ? string.Join(" ", arr) : null;
                        code = "VALIDATION_ERROR";
                    }
                }
                // Format 3: { code, message } flat ou { title, detail } ProblemDetails
                else
                {
                    code = TryString(root, "code");
                    msg = TryString(root, "message") ?? TryString(root, "detail") ?? TryString(root, "title");
                }
            }
        }
        catch
        {
            // body não é JSON ou parse falhou — usa fallback por status.
        }

        msg ??= FallbackMessageForStatus(status);
        throw new ApiException(status, code, msg);
    }

    private static string? TryString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string FallbackMessageForStatus(int status) => status switch
    {
        400 => "Dados inválidos. Revise os campos e tente novamente.",
        401 => "Sessão expirada. Faça login novamente.",
        403 => "Você não tem permissão para esta ação.",
        404 => "Recurso não encontrado.",
        409 => "Conflito de dados. O registro já existe ou está em uso.",
        422 => "Não foi possível processar — algum dado está inconsistente.",
        500 => "Erro interno no servidor. Tente novamente em instantes.",
        502 => "Serviço temporariamente indisponível.",
        503 => "Serviço temporariamente indisponível. Tente novamente em instantes.",
        _ => $"Erro HTTP {status}. Tente novamente — se persistir, contate o suporte."
    };

    private static T UnwrapData<T>(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
            throw new ApiException(0, "BAD_RESPONSE", "Resposta inesperada da API (sem campo 'data').");
        return data.Deserialize<T>(JsonOptions)
            ?? throw new ApiException(0, "BAD_RESPONSE", "API devolveu corpo vazio quando esperávamos dados.");
    }

    public async Task<T> GetAsync<T>(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Get, path));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    public async Task<JsonElement> GetRawAsync(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Get, path));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    /// <summary>
    /// GET sem desempacotar envelope <c>data</c>. Usado pelos endpoints
    /// <c>/api/mobile/*</c> que devolvem JSON cru (não envelopado como os
    /// admin/empresa endpoints). EnsureSuccess + parse direto pro tipo destino.
    /// </summary>
    public async Task<T> GetJsonAsync<T>(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Get, path));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            throw new ApiException(0, "EMPTY_RESPONSE", "API devolveu resposta vazia inesperadamente.");
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new ApiException(0, "BAD_RESPONSE", "API devolveu corpo vazio.");
    }

    /// <summary>POST sem desempacotar envelope <c>data</c>. Mesmo motivo de <see cref="GetJsonAsync{T}"/>.</summary>
    public async Task<T> PostJsonAsync<T>(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Post, path, body));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            throw new ApiException(0, "EMPTY_RESPONSE", "API devolveu resposta vazia inesperadamente.");
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new ApiException(0, "BAD_RESPONSE", "API devolveu corpo vazio.");
    }

    public async Task<T> PostAsync<T>(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Post, path, body));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    /// <summary>
    /// Mantém o envelope cru pra o caller inspecionar `error`/`data` (usado pelo
    /// Login que precisa diferenciar credenciais inválidas de outros erros).
    /// Não faz EnsureSuccessOrThrow; apenas diferencia 401 (sessão expirada).
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

    public async Task<JsonElement> PostMultipartRawAsync(string path, MultipartFormDataContent multipart)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = multipart };
        using var response = await httpClient.SendAsync(request);
        ThrowIfUnauthorized(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
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
        await EnsureSuccessOrThrowAsync(response);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return UnwrapData<T>(json.RootElement);
    }

    /// <summary>PUT sem corpo de retorno relevante (204 NoContent ou similar).</summary>
    public async Task PutAsync(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Put, path, body));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
    }

    public async Task PatchRawAsync(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Patch, path, body));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
    }

    /// <summary>
    /// PUT com corpo de resposta opcional. Retorna JsonElement vazio (default) quando
    /// API responde 204 NoContent — isso é comportamento esperado, não erro silencioso.
    /// Use <see cref="PutAsync"/> quando não há corpo de resposta e não precisa inspecionar.
    /// </summary>
    public async Task<JsonElement> PutRawAsync(string path, object body)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Put, path, body));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return default; // 204 NoContent é válido — caller não precisa de corpo
        using var json = JsonDocument.Parse(content);
        return json.RootElement.Clone();
    }

    public async Task DeleteAsync(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Delete, path));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
    }

    public async Task<(byte[] Bytes, string ContentType)> GetBytesAsync(string path)
    {
        using var response = await httpClient.SendAsync(BuildRequest(HttpMethod.Get, path));
        ThrowIfUnauthorized(response);
        await EnsureSuccessOrThrowAsync(response);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var ct = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        return (bytes, ct);
    }
}
