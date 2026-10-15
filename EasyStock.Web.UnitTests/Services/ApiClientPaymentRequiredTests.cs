using System.Net;
using System.Text;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// Trava o 402 de limite de recurso do plano (LIMITE_PLANO). O 402 de bloqueio de
/// assinatura (SubscriptionGate) saiu na poda P02.
/// </summary>
public class ApiClientPaymentRequiredTests
{
    private static ApiClient ClientRespondendo(string body)
    {
        var http = new HttpClient(new StubHandler(body)) { BaseAddress = new Uri("http://api.test/") };
        return new ApiClient(http, NullLogger<ApiClient>.Instance);
    }

    [Fact]
    public async Task Limite_402_com_recurso_continua_LIMITE_PLANO()
    {
        var api = ClientRespondendo("{\"error\":{\"recurso\":\"lojas\"}}");

        var r = await api.GetAsync<object>("lojas");

        r.ErrorCode.Should().Be("LIMITE_PLANO:lojas");
    }

    [Fact]
    public async Task Body_402_vazio_mantem_LIMITE_PLANO()
    {
        var api = ClientRespondendo(string.Empty);

        var r = await api.GetAsync<object>("lojas");

        r.ErrorCode.Should().Be("LIMITE_PLANO");
    }

    [Fact]
    public async Task Code_402_desconhecido_nao_vira_bloqueio()
    {
        var api = ClientRespondendo("{\"error\":{\"code\":\"OUTRA_COISA\"}}");

        var r = await api.GetAsync<object>("lojas");

        r.ErrorCode.Should().Be("LIMITE_PLANO");
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.PaymentRequired)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
