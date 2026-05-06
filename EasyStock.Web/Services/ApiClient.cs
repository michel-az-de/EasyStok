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

    public async Task<ApiResult<T>> PostAsync<T>(string path, object body, string? idempotencyKey = null)
    {
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
            // Idempotencia: P0-3. Auto-gera UUID se path bate com whitelist conhecida
            // ou se chamador explicitar uma chave. Server (whitelist em Program.cs)
            // ignora chaves em paths nao-criticos.
            var key = idempotencyKey ?? IdempotencyKeyHelper.AutoGenerateIfApplicable(path);
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

            var response = await http.SendAsync(request);
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
                // 204 NoContent (e respostas com corpo vazio em geral): tratar como sucesso
                // sem tentar deserializar — o tipo T pode ser object/bool/etc.
                if (string.IsNullOrWhiteSpace(json))
                    return ApiResult<T>.Ok(default!) with { HttpStatus = status };

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
                log.LogWarning(ex, "JSON deserialization to {Type} failed (HTTP {Status}).", typeof(T).Name, status);
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
            HttpStatusCode.PaymentRequired => await ParseLimitError<T>(response, status),
            HttpStatusCode.Forbidden =>
                ApiResult<T>.Fail("PERMISSAO_INSUFICIENTE", "Você não tem permissão para esta ação.", status),
            HttpStatusCode.NotFound =>
                ApiResult<T>.Fail("NOT_FOUND", "Recurso não encontrado.", status),
            HttpStatusCode.TooManyRequests =>
                ApiResult<T>.Fail("LIMITE_IA", "Cota de IA esgotada.", status),
            HttpStatusCode.InternalServerError =>
                ApiResult<T>.Fail("SERVER_ERROR", "Erro no servidor. Tente novamente.", status),
            HttpStatusCode.BadRequest => await ParseBodyError<T>(response, status),
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

            // Corpo vazio: 400/409/422 sem JSON. Não vale a pena dizer "Erro na requisição"
            // genérico — orientamos pelo status.
            if (string.IsNullOrWhiteSpace(json))
                return ApiResult<T>.Fail("API_ERROR", FallbackMessageForStatus(status), status);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Format 1: { error: { code, message, detail } }
            if (root.TryGetProperty("error", out var errEl))
            {
                var code = TryString(errEl, "code") ?? "API_ERROR";
                var msg = TryString(errEl, "detail") ?? TryString(errEl, "message");
                return ApiResult<T>.Fail(code, ResolveMessage(code, msg, status), status);
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
                var joined = messages.Count > 0 ? string.Join(" ", messages) : FallbackMessageForStatus(status);
                return ApiResult<T>.Fail("VALIDATION_ERROR", joined, status);
            }

            // Format 3: { code, message } (flat) ou { title } (ProblemDetails)
            var flatCode = TryString(root, "code") ?? "API_ERROR";
            var flatMsg = TryString(root, "message")
                ?? TryString(root, "detail")
                ?? TryString(root, "title");
            return ApiResult<T>.Fail(flatCode, ResolveMessage(flatCode, flatMsg, status), status);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "Could not parse error body from HTTP {Status} response on path handling", status);
            return ApiResult<T>.Fail("API_ERROR", FallbackMessageForStatus(status), status);
        }
    }

    private static string? TryString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string ResolveMessage(string code, string? message, int status)
    {
        // Se a API devolveu mensagem útil, prevalece — backend conhece melhor o contexto.
        if (!string.IsNullOrWhiteSpace(message) &&
            !message.Equals("Erro na requisicao.", StringComparison.OrdinalIgnoreCase) &&
            !message.Equals("Erro na requisição.", StringComparison.OrdinalIgnoreCase) &&
            !message.Equals("Bad Request", StringComparison.OrdinalIgnoreCase))
            return message;

        // Caso contrário tenta mapear pelo código conhecido.
        if (KnownErrorMessages.TryGetValue(code, out var known))
            return known;

        return FallbackMessageForStatus(status);
    }

    private static string FallbackMessageForStatus(int status) => status switch
    {
        400 => "Dados inválidos no formulário. Revise os campos e tente novamente.",
        404 => "Recurso não encontrado.",
        409 => "Conflito de dados. Esse registro já existe ou está em uso.",
        422 => "Não foi possível processar — algum dado está inconsistente.",
        500 => "Erro interno no servidor. Tente novamente em alguns instantes.",
        503 => "Serviço temporariamente indisponível. Tente novamente em instantes.",
        _   => $"Erro HTTP {status}. Tente novamente — se persistir, contate o suporte."
    };

    // Códigos conhecidos do backend → mensagens amigáveis.
    // Quando o backend devolve só o código (sem mensagem), traduzimos aqui pra evitar
    // toasts crípticos. A lista cresce conforme novos códigos aparecerem nos handlers da API.
    private static readonly Dictionary<string, string> KnownErrorMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CATEGORIA_INVALIDA"] = "Categoria inválida ou não encontrada.",
        ["CATEGORIA_DUPLICADA"] = "Já existe uma categoria com esse nome.",
        ["CATEGORIA_EM_USO"] = "Esta categoria está em uso por produtos e não pode ser excluída.",
        ["CNPJ_DUPLICADO"] = "Este CNPJ já está cadastrado.",
        ["CPF_DUPLICADO"] = "Este CPF já está cadastrado.",
        ["EMAIL_DUPLICADO"] = "Este e-mail já está em uso.",
        ["EMAIL_INVALIDO"] = "E-mail inválido.",
        ["DOCUMENTO_INVALIDO"] = "Documento (CPF/CNPJ) inválido.",
        ["SKU_DUPLICADO"] = "Já existe um produto com esse SKU.",
        ["PRODUTO_NAO_ENCONTRADO"] = "Produto não encontrado.",
        ["ESTOQUE_INSUFICIENTE"] = "Estoque insuficiente para esta operação.",
        ["FORNECEDOR_DUPLICADO"] = "Já existe um fornecedor com esse documento.",
        ["CLIENTE_DUPLICADO"] = "Já existe um cliente com esse documento.",
        ["CAIXA_JA_ABERTO"] = "O caixa do dia já está aberto.",
        ["CAIXA_NAO_ABERTO"] = "É necessário abrir o caixa antes de registrar movimentos.",
        ["CAIXA_FECHADO"] = "O caixa do dia já foi fechado.",
        ["EMPRESA_INVALIDA"] = "Loja não identificada. Selecione uma loja e tente novamente.",
        ["LOJA_NAO_SELECIONADA"] = "Selecione uma loja antes de continuar.",
        ["LOJA_DUPLICADA"] = "Já existe uma loja com esse nome.",
        ["VALIDATION_ERROR"] = "Há campos inválidos no formulário. Revise e tente novamente.",
        ["NOT_FOUND"] = "Recurso não encontrado.",
        ["PERMISSAO_INSUFICIENTE"] = "Você não tem permissão para esta ação.",
        ["AUTH_TOKEN_EXPIRED"] = "Sessão expirada. Faça login novamente.",
        ["LIMITE_PLANO"] = "Limite do seu plano atingido.",
        ["LIMITE_IA"] = "Cota de IA esgotada.",
        ["TIMEOUT"] = "O servidor demorou para responder. Tente novamente.",
        ["NETWORK_ERROR"] = "Não foi possível conectar ao servidor. Verifique sua conexão.",
        ["SERVER_ERROR"] = "Erro interno no servidor. Tente novamente em instantes."
    };
    private async Task<ApiResult<T>> ParseLimitError<T>(HttpResponseMessage response, int status)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? recurso = null;
            if (root.TryGetProperty("error", out var errEl) && errEl.TryGetProperty("recurso", out var r))
                recurso = r.GetString();
            var code = recurso != null ? $"LIMITE_PLANO:{recurso}" : "LIMITE_PLANO";
            return ApiResult<T>.Fail(code, "Limite do plano atingido.", status);
        }
        catch
        {
            return ApiResult<T>.Fail("LIMITE_PLANO", "Limite do plano atingido.", status);
        }
    }

    private static bool IsPagedResultType(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PagedResult<>);
}
