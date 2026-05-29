using System.Net;
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

    public Task<ApiResult<T>> GetAsync<T>(string path) =>
        WrapHttpCallAsync<T>(() => http.GetAsync(path), "GET", path);

    public Task<ApiResult<T>> PostAsync<T>(string path, object body, string? idempotencyKey = null) =>
        WrapHttpCallAsync<T>(async () =>
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
            // Idempotencia: P0-3. Auto-gera UUID se path bate com whitelist conhecida
            // ou se chamador explicitar uma chave. Server (whitelist em Program.cs)
            // ignora chaves em paths nao-criticos.
            var key = idempotencyKey ?? IdempotencyKeyHelper.AutoGenerateIfApplicable(path);
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            return await http.SendAsync(request);
        }, "POST", path);

    public Task<ApiResult<T>> PutAsync<T>(string path, object body) =>
        WrapHttpCallAsync<T>(() =>
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            return http.PutAsync(path, content);
        }, "PUT", path);

    public Task<ApiResult<T>> PatchAsync<T>(string path, object body) =>
        WrapHttpCallAsync<T>(() =>
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            return http.PatchAsync(path, content);
        }, "PATCH", path);

    public async Task<ApiResult<bool>> DeleteAsync(string path)
    {
        // Wrapper genérico WrapHttpCallAsync usa ParseResponse para o caminho de sucesso,
        // que não cobre o curto-circuito Ok(true) sem body deste verbo — try/catch local
        // permanece, apenas logging de timeout é alinhado aos demais verbos.
        try
        {
            var response = await http.DeleteAsync(path);
            if (response.IsSuccessStatusCode) return ApiResult<bool>.Ok(true);
            return await ParseErrorResponse<bool>(response);
        }
        catch (TaskCanceledException)
        {
            log.LogWarning("DELETE {Path} timed out", path);
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
                return ApiResult<Stream>.Fail("HTTP_ERROR",
                    $"Erro HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode,
                    ExtractCorrelationId(response));
            var stream = await response.Content.ReadAsStreamAsync();
            return ApiResult<Stream>.Ok(stream) with { CorrelationId = ExtractCorrelationId(response) };
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
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json")
            };
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                var errorResult = await ParseErrorResponse<Stream>(response);
                response.Dispose();
                return errorResult;
            }
            var stream = await response.Content.ReadAsStreamAsync();
            // ResponseStream disposes the HttpResponseMessage when the stream is closed,
            // preventing socket exhaustion on SSE/streaming connections.
            return ApiResult<Stream>.Ok(new ResponseStream(stream, response));
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

    /// <summary>
    /// Wraps a response body <see cref="Stream"/> and disposes the owning
    /// <see cref="HttpResponseMessage"/> when the stream itself is disposed,
    /// ensuring the underlying socket is released after streaming (SSE) responses.
    /// </summary>
    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public Task<ApiResult<T>> PostMultipartAsync<T>(string path, MultipartFormDataContent form) =>
        WrapHttpCallAsync<T>(() => http.PostAsync(path, form), "POST multipart", path);

    private async Task<ApiResult<T>> WrapHttpCallAsync<T>(
        Func<Task<HttpResponseMessage>> httpCall,
        string method,
        string path)
    {
        try
        {
            var response = await httpCall();
            return await ParseResponse<T>(response);
        }
        catch (TaskCanceledException)
        {
            log.LogWarning("{Method} {Path} timed out", method, path);
            return ApiResult<T>.Fail("TIMEOUT", "Servidor não respondeu. Verifique sua conexão.");
        }
        catch (HttpRequestException ex)
        {
            log.LogError(ex, "Network error on {Method} {Path}", method, path);
            return ApiResult<T>.Fail("NETWORK_ERROR", "Não foi possível conectar ao servidor.");
        }
    }

    private async Task<ApiResult<T>> ParseResponse<T>(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var correlationId = ExtractCorrelationId(response);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var json = await response.Content.ReadAsStringAsync();
                // 204 NoContent (e respostas com corpo vazio em geral): tratar como sucesso
                // sem tentar deserializar — o tipo T pode ser object/bool/etc.
                if (string.IsNullOrWhiteSpace(json))
                    return ApiResult<T>.Ok(default!) with { HttpStatus = status, CorrelationId = correlationId };

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                JsonElement payload = IsPagedResultType(typeof(T))
                    ? root
                    : root.TryGetProperty("data", out var d) ? d : root;

                var data = payload.Deserialize<T>(JsonOpts);
                return data is null
                    ? ApiResult<T>.Fail("EMPTY_RESPONSE", "Resposta vazia do servidor.", status, correlationId)
                    : ApiResult<T>.Ok(data) with { HttpStatus = status, CorrelationId = correlationId };
            }
            catch (JsonException ex)
            {
                log.LogWarning(ex, "JSON deserialization to {Type} failed (HTTP {Status}, CID {CorrelationId}).",
                    typeof(T).Name, status, correlationId);
                return ApiResult<T>.Fail("PARSE_ERROR", "Erro ao processar resposta do servidor.", status, correlationId);
            }
        }

        return await ParseErrorResponse<T>(response);
    }

    private static string? ExtractCorrelationId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Correlation-Id", out var values))
            return values.FirstOrDefault();
        return null;
    }

    private async Task<ApiResult<T>> ParseErrorResponse<T>(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        var cid = ExtractCorrelationId(response);

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                ApiResult<T>.Fail("AUTH_TOKEN_EXPIRED", "Sessão expirada. Faça login novamente.", status, cid),
            HttpStatusCode.PaymentRequired => await ParseLimitError<T>(response, status, cid),
            HttpStatusCode.Forbidden =>
                ApiResult<T>.Fail("PERMISSAO_INSUFICIENTE", "Você não tem permissão para esta ação.", status, cid),
            HttpStatusCode.NotFound =>
                ApiResult<T>.Fail("NOT_FOUND", "Recurso não encontrado.", status, cid),
            HttpStatusCode.TooManyRequests =>
                ApiResult<T>.Fail("LIMITE_IA", "Cota de IA esgotada.", status, cid),
            HttpStatusCode.InternalServerError => await ParseBodyError<T>(response, status, cid),
            HttpStatusCode.BadRequest => await ParseBodyError<T>(response, status, cid),
            HttpStatusCode.Conflict => await ParseBodyError<T>(response, status, cid),
            HttpStatusCode.UnprocessableEntity => await ParseBodyError<T>(response, status, cid),
            // Demais 4xx/5xx: tenta extrair detail do envelope ApiError. Se vier vazio,
            // ParseBodyError já cai em UserFacingErrors.FallbackForStatus.
            _ => await ParseBodyError<T>(response, status, cid)
        };
    }

    private async Task<ApiResult<T>> ParseBodyError<T>(HttpResponseMessage response, int status, string? cid = null)
    {
        cid ??= ExtractCorrelationId(response);
        try
        {
            var json = await response.Content.ReadAsStringAsync();

            // Corpo vazio: 400/409/422 sem JSON. Não vale a pena dizer "Erro na requisição"
            // genérico — orientamos pelo status.
            if (string.IsNullOrWhiteSpace(json))
                return ApiResult<T>.Fail("API_ERROR", UserFacingErrors.FallbackForStatus(status), status, cid);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Format 1: { error: { code, message, detail } }
            if (root.TryGetProperty("error", out var errEl))
            {
                var code = TryString(errEl, "code") ?? "API_ERROR";
                var msg = TryString(errEl, "detail") ?? TryString(errEl, "message");
                return ApiResult<T>.Fail(code, ResolveMessage(code, msg, status), status, cid);
            }

            // Format 2: FluentValidation / ASP.NET { errors: { "Field": ["msg", ...] } }
            if (root.TryGetProperty("errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Object)
            {
                var messages = new List<string>();
                foreach (var prop in errorsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            var s = item.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) messages.Add(s);
                        }
                    }
                }
                var joined = messages.Count > 0 ? string.Join(" ", messages) : UserFacingErrors.FallbackForStatus(status);
                return ApiResult<T>.Fail("VALIDATION_ERROR", joined, status, cid);
            }

            // Format 3: { code, message } (flat) ou { title } (ProblemDetails)
            var flatCode = TryString(root, "code") ?? "API_ERROR";
            var flatMsg = TryString(root, "message")
                ?? TryString(root, "detail")
                ?? TryString(root, "title");
            return ApiResult<T>.Fail(flatCode, ResolveMessage(flatCode, flatMsg, status), status, cid);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Could not parse error body from HTTP {Status} response on path handling", status);
            return ApiResult<T>.Fail("API_ERROR", UserFacingErrors.FallbackForStatus(status), status, cid);
        }
    }

    private static string? TryString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string ResolveMessage(string code, string? message, int status) =>
        UserFacingErrors.Sanitize(code, message, status);

    private async Task<ApiResult<T>> ParseLimitError<T>(HttpResponseMessage response, int status, string? cid = null)
    {
        cid ??= ExtractCorrelationId(response);
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? recurso = null;
            if (root.TryGetProperty("error", out var errEl) && errEl.TryGetProperty("recurso", out var r))
                recurso = r.GetString();
            var code = recurso != null ? $"LIMITE_PLANO:{recurso}" : "LIMITE_PLANO";
            return ApiResult<T>.Fail(code, "Limite do plano atingido.", status, cid);
        }
        catch
        {
            return ApiResult<T>.Fail("LIMITE_PLANO", "Limite do plano atingido.", status, cid);
        }
    }

    private static bool IsPagedResultType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PagedResult<>);
}
