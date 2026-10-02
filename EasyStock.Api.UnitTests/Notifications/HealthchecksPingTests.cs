using EasyStock.Infra.Notifications.Hosting;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// N1, ver e avisar: o Worker pinga o Healthchecks.io a cada minuto enquanto os heartbeats e o backlog estão saudáveis e
/// chama <c>/fail</c> quando não; sem o ping, o serviço alerta pela ausência.
/// </summary>
public class HealthchecksPingTests
{
    private sealed class HandlerQueGrava : HttpMessageHandler
    {
        public List<Uri> Chamadas { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Chamadas.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private static (HealthchecksPingService Servico, HandlerQueGrava Handler, HealthCheckService Checks) Montar(
        HealthStatus status, string? url = "https://hc-ping.com/abc-123")
    {
        var entradas = new Dictionary<string, HealthReportEntry>
        {
            ["Backlog"] = new(status, "teste", TimeSpan.Zero, null, null),
        };
        var checks = Substitute.For<HealthCheckService>();
        checks.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(new HealthReport(entradas, TimeSpan.Zero));

        var handler = new HandlerQueGrava();
        var fabrica = Substitute.For<IHttpClientFactory>();
        fabrica.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        var servico = new HealthchecksPingService(
            checks, fabrica, Options.Create(new NotificacoesMonitoramentoOptions { PingUrl = url }),
            NullLogger<HealthchecksPingService>.Instance);
        return (servico, handler, checks);
    }

    [Fact]
    public async Task Pinga_so_com_os_checks_saudaveis_e_chama_fail_quando_nao()
    {
        var saudavel = Montar(HealthStatus.Healthy);
        await saudavel.Servico.PingarUmaVezAsync(CancellationToken.None);
        saudavel.Handler.Chamadas.Should().ContainSingle().Which.ToString().Should().Be("https://hc-ping.com/abc-123");

        var degradado = Montar(HealthStatus.Degraded);
        await degradado.Servico.PingarUmaVezAsync(CancellationToken.None);
        degradado.Handler.Chamadas.Should().ContainSingle().Which.ToString().Should().Be("https://hc-ping.com/abc-123/fail");

        var inoperante = Montar(HealthStatus.Unhealthy, url: "https://hc-ping.com/abc-123/");
        await inoperante.Servico.PingarUmaVezAsync(CancellationToken.None);
        inoperante.Handler.Chamadas.Should().ContainSingle().Which.ToString().Should().Be("https://hc-ping.com/abc-123/fail",
            "a barra final da URL não duplica");
    }

    [Fact]
    public async Task So_avalia_os_checks_do_motor_de_notificacoes()
    {
        var (servico, _, checks) = Montar(HealthStatus.Healthy);

        await servico.PingarUmaVezAsync(CancellationToken.None);

        await checks.Received(1).CheckHealthAsync(
            Arg.Is<Func<HealthCheckRegistration, bool>?>(predicado => predicado != null
                && predicado(new HealthCheckRegistration("a", Substitute.For<IHealthCheck>(), null, new[] { "notificacoes" }))
                && predicado(new HealthCheckRegistration("b", Substitute.For<IHealthCheck>(), null, new[] { "dispatcher" }))
                && !predicado(new HealthCheckRegistration("c", Substitute.For<IHealthCheck>(), null, new[] { "ready" }))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sem_PingUrl_nao_chama_nada()
    {
        var (servico, handler, checks) = Montar(HealthStatus.Healthy, url: null);

        await servico.PingarUmaVezAsync(CancellationToken.None);

        handler.Chamadas.Should().BeEmpty();
        await checks.DidNotReceiveWithAnyArgs().CheckHealthAsync(default, default);
    }

    [Fact]
    public async Task Falha_do_proprio_ping_nao_derruba_o_servico()
    {
        var (servico, _, checks) = Montar(HealthStatus.Healthy);
        checks.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns<HealthReport>(_ => throw new HttpRequestException("healthchecks.io fora"));

        var act = async () => await servico.PingarUmaVezAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
