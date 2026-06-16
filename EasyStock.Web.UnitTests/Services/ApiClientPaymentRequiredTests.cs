using System.Net;
using System.Text;
using EasyStock.Web.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// Trava a distinção dos DOIS 402 que a Api emite (#619/#620): bloqueio de assinatura
/// (SubscriptionGate: trial vencido/suspenso/cancelado) vs. limite de recurso do plano.
/// Sem essa distinção o tenant com trial vencido caía em loop de "criar loja" em vez de
/// ir para a landing de assinatura.
/// </summary>
public class ApiClientPaymentRequiredTests
{
    private static ApiClient ClientRespondendo(string body)
    {
        var http = new HttpClient(new StubHandler(body)) { BaseAddress = new Uri("http://api.test/") };
        return new ApiClient(http, NullLogger<ApiClient>.Instance);
    }

    [Theory]
    [InlineData("TRIAL_EXPIRED")]
    [InlineData("NO_SUBSCRIPTION")]
    [InlineData("SUBSCRIPTION_SUSPENDED")]
    [InlineData("SUBSCRIPTION_CANCELLED")]
    [InlineData("SUBSCRIPTION_EXPIRED")]
    public async Task Gate_402_com_code_de_bloqueio_vira_ASSINATURA_BLOQUEADA(string code)
    {
        var api = ClientRespondendo(
            $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"x\",\"upgradeUrl\":\"/assinatura\"}}}}");

        var r = await api.GetAsync<object>("lojas");

        r.Success.Should().BeFalse();
        r.ErrorCode.Should().Be($"ASSINATURA_BLOQUEADA:{code}");
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
