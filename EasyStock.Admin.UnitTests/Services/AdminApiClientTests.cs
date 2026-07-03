using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Admin.Services;
using FluentAssertions;

namespace EasyStock.Admin.UnitTests.Services;

/// <summary>
/// Trava o contrato de erro do AdminApiClient (issue 820): os 3 envelopes da API
/// viram ApiException com mensagem AMIGAVEL (segura pra toast), 401 vira
/// SessionExpiredException (dispara o fluxo de login) e resposta sem corpo no
/// PostRawAsync vira envelope sintetico EMPTY_RESPONSE. Quebrar qualquer um
/// desses mapeamentos degrada TODAS as telas do Admin ao mesmo tempo.
/// </summary>
public class AdminApiClientTests
{
    private static AdminApiClient Cliente(HttpStatusCode status, string? body, string contentType = "application/json")
        => Cliente(_ => Resposta(status, body, contentType));

    private static AdminApiClient Cliente(Func<HttpRequestMessage, HttpResponseMessage> respond)
        => new(new HttpClient(new ScriptedHandler(respond)) { BaseAddress = new Uri("http://api.test/") });

    private static HttpResponseMessage Resposta(HttpStatusCode status, string? body, string contentType = "application/json")
    {
        var resp = new HttpResponseMessage(status);
        if (body is not null) resp.Content = new StringContent(body, Encoding.UTF8, contentType);
        return resp;
    }

    [Fact]
    public async Task GetAsync_desempacota_o_envelope_data()
    {
        var api = Cliente(HttpStatusCode.OK, "{\"data\":{\"nome\":\"Casa da Baba\"}}");

        var el = await api.GetAsync<JsonElement>("api/admin/tenants/x");

        el.GetProperty("nome").GetString().Should().Be("Casa da Baba");
    }

    [Fact]
    public async Task Erro_com_envelope_error_vira_ApiException_com_mensagem_amigavel()
    {
        var api = Cliente(HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":\"CUPOM_DUPLICADO\",\"message\":\"Ja existe cupom com este codigo.\"}}");

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetAsync<JsonElement>("api/x"));

        ex.HttpStatus.Should().Be(400);
        ex.ErrorCode.Should().Be("CUPOM_DUPLICADO");
        ex.Message.Should().Be("Ja existe cupom com este codigo.");
    }

    [Fact]
    public async Task Erro_de_validacao_FluentValidation_vira_mensagem_do_primeiro_campo()
    {
        var api = Cliente(HttpStatusCode.UnprocessableEntity,
            "{\"errors\":{\"Nome\":[\"Nome e obrigatorio.\",\"Minimo 2 caracteres.\"]}}");

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetAsync<JsonElement>("api/x"));

        ex.ErrorCode.Should().Be("VALIDATION_ERROR");
        ex.Message.Should().Contain("Nome e obrigatorio.").And.Contain("Minimo 2 caracteres.");
    }

    [Fact]
    public async Task Erro_404_sem_corpo_usa_fallback_em_portugues()
    {
        var api = Cliente(HttpStatusCode.NotFound, body: null);

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetAsync<JsonElement>("api/x"));

        ex.HttpStatus.Should().Be(404);
        ex.Message.Should().Be("Recurso não encontrado.");
    }

    [Fact]
    public async Task Erro_500_com_corpo_nao_json_usa_fallback_por_status()
    {
        var api = Cliente(HttpStatusCode.InternalServerError, "<html>proxy error</html>", "text/html");

        var ex = await Assert.ThrowsAsync<ApiException>(() => api.GetRawAsync("api/x"));

        ex.HttpStatus.Should().Be(500);
        ex.Message.Should().Be("Erro interno no servidor. Tente novamente em instantes.");
    }

    [Fact]
    public async Task Status_401_vira_SessionExpiredException()
    {
        var api = Cliente(HttpStatusCode.Unauthorized, body: null);

        await Assert.ThrowsAsync<SessionExpiredException>(() => api.GetAsync<JsonElement>("api/x"));
    }

    [Fact]
    public async Task PostRawAsync_sem_corpo_gera_envelope_sintetico_EMPTY_RESPONSE()
    {
        var api = Cliente(HttpStatusCode.InternalServerError, body: null);

        var el = await api.PostRawAsync("api/auth/login", new { });

        el.GetProperty("error").GetProperty("code").GetString().Should().Be("EMPTY_RESPONSE");
        el.GetProperty("error").GetProperty("message").GetString().Should().Contain("500");
    }
}
