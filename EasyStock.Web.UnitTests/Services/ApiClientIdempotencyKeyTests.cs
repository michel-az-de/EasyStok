using System.Net;
using System.Text;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// issue 917 Fase B: a chave explicita do chamador (gerada no BROWSER, estavel entre
/// retries da mesma intencao) tem que vencer o fallback por-request de
/// <see cref="IdempotencyKeyHelper"/> — sem isso a protecao do servidor era ilusoria
/// (Guid.NewGuid() por request fazia dois cliques do browser gerarem chaves distintas).
/// </summary>
public class ApiClientIdempotencyKeyTests
{
    private static (ApiClient api, CapturingHandler handler) ClientCapturando()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        return (new ApiClient(http, NullLogger<ApiClient>.Instance), handler);
    }

    [Fact]
    public async Task Chave_explicita_do_chamador_vence_o_fallback_por_request()
    {
        var (api, handler) = ClientCapturando();

        await api.PostAsync<object>("pedidos/abc/pagamentos", new { valor = 10 }, idempotencyKey: "chave-estavel-do-browser");

        handler.LastRequest!.Headers.TryGetValues("Idempotency-Key", out var values).Should().BeTrue();
        values!.Single().Should().Be("chave-estavel-do-browser");
    }

    [Fact]
    public async Task Chave_explicita_vazia_ou_whitespace_cai_no_fallback_por_request()
    {
        var (api, handler) = ClientCapturando();

        // "pedidos" esta na whitelist do fallback (IdempotencyKeyHelper) -- se a chave
        // explicita vazia fosse tratada como "mandou algo", a request sairia SEM header
        // nenhum (regressao: pior que o fallback que ja existia antes desta mudanca).
        await api.PostAsync<object>("pedidos/abc/pagamentos", new { valor = 10 }, idempotencyKey: "   ");

        handler.LastRequest!.Headers.TryGetValues("Idempotency-Key", out var values).Should().BeTrue();
        values!.Single().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Sem_chave_explicita_em_rota_fora_da_whitelist_nao_manda_header()
    {
        var (api, handler) = ClientCapturando();

        await api.PostAsync<object>("produtos", new { nome = "x" });

        handler.LastRequest!.Headers.TryGetValues("Idempotency-Key", out _).Should().BeFalse();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
