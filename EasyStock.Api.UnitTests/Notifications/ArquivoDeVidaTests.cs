using EasyStock.Infra.Notifications.Hosting;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #2107 (F2): o Worker não tem HTTP, então prova que está vivo regravando um arquivo enquanto os loops do motor batem
/// heartbeat. O healthcheck do Docker lê a idade do arquivo. O backlog fica de fora: provider stub em produção deixa o
/// backlog Degraded para sempre, e isso não é o Worker parado.
/// </summary>
public sealed class ArquivoDeVidaTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), $"ez-worker-vivo-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (File.Exists(_arquivo)) File.Delete(_arquivo);
    }

    private static HealthCheckService Checks(HealthStatus status)
    {
        var checks = Substitute.For<HealthCheckService>();
        checks.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(new HealthReport(
                new Dictionary<string, HealthReportEntry> { ["Loops"] = new(status, "teste", TimeSpan.Zero, null, null) },
                TimeSpan.Zero));
        return checks;
    }

    private ArquivoDeVidaService Servico(HealthCheckService checks, string? arquivo) =>
        new(checks, Options.Create(new NotificacoesMonitoramentoOptions { ArquivoDeVida = arquivo }),
            NullLogger<ArquivoDeVidaService>.Instance);

    [Fact]
    public async Task Loops_saudaveis_regravam_o_arquivo()
    {
        await Servico(Checks(HealthStatus.Healthy), _arquivo).MarcarUmaVezAsync(CancellationToken.None);

        File.Exists(_arquivo).Should().BeTrue();
        File.GetLastWriteTimeUtc(_arquivo).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(HealthStatus.Degraded)]
    [InlineData(HealthStatus.Unhealthy)]
    public async Task Loops_parados_nao_tocam_o_arquivo(HealthStatus status)
    {
        File.WriteAllText(_arquivo, "antigo");
        var antes = DateTime.UtcNow.AddMinutes(-10);
        File.SetLastWriteTimeUtc(_arquivo, antes);

        await Servico(Checks(status), _arquivo).MarcarUmaVezAsync(CancellationToken.None);

        File.GetLastWriteTimeUtc(_arquivo).Should().BeCloseTo(antes, TimeSpan.FromSeconds(1),
            "arquivo velho é o que faz o Docker marcar o Worker como unhealthy");
    }

    [Fact]
    public async Task So_avalia_os_heartbeats_dos_loops()
    {
        var checks = Checks(HealthStatus.Healthy);

        await Servico(checks, _arquivo).MarcarUmaVezAsync(CancellationToken.None);

        await checks.Received(1).CheckHealthAsync(
            Arg.Is<Func<HealthCheckRegistration, bool>?>(predicado => predicado != null
                && predicado(new HealthCheckRegistration("a", Substitute.For<IHealthCheck>(), null, new[] { "dispatcher" }))
                && !predicado(new HealthCheckRegistration("b", Substitute.For<IHealthCheck>(), null, new[] { "notificacoes" }))),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Sem_caminho_configurado_nao_faz_nada(string? arquivo)
    {
        var checks = Checks(HealthStatus.Healthy);

        await Servico(checks, arquivo).MarcarUmaVezAsync(CancellationToken.None);

        await checks.DidNotReceiveWithAnyArgs().CheckHealthAsync(default, default);
    }
}
