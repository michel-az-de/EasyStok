using EasyStock.Application.Services.Notifications;
using EasyStock.Infra.Notifications.Hosting;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

public class NotificationsHostingHealthCheckTests
{
    private static IOptions<NotificationsHostingOptions> Opts(NotificationsHostingMode mode,
        int dispatcherIntervalMs = 10_000,
        int avaliadorSeconds = 60,
        int coletorSeconds = 300) =>
        Options.Create(new NotificationsHostingOptions
        {
            Mode = mode,
            DispatcherPollingIntervalMs = dispatcherIntervalMs,
            AvaliadorIntervalSeconds = avaliadorSeconds,
            ColetorIntervalSeconds = coletorSeconds,
        });

    [Fact]
    public async Task Mode_Disabled_retorna_Healthy_sem_olhar_heartbeat()
    {
        var heartbeat = Substitute.For<INotificationsLoopHeartbeat>();
        var sut = new NotificationsHostingHealthCheck(Opts(NotificationsHostingMode.Disabled), heartbeat);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        heartbeat.DidNotReceive().LastBeat(Arg.Any<string>());
    }

    [Fact]
    public async Task Mode_Hosted_sem_heartbeat_retorna_Unhealthy()
    {
        var heartbeat = Substitute.For<INotificationsLoopHeartbeat>();
        heartbeat.LastBeat(Arg.Any<string>()).Returns((DateTimeOffset?)null);
        var sut = new NotificationsHostingHealthCheck(Opts(NotificationsHostingMode.Hosted), heartbeat);

        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("dispatcher")
            .And.Contain("avaliador")
            .And.Contain("coletor");
    }

    [Fact]
    public async Task Mode_Hosted_heartbeats_recentes_retorna_Healthy()
    {
        var heartbeat = Substitute.For<INotificationsLoopHeartbeat>();
        var agora = DateTimeOffset.UtcNow;
        heartbeat.LastBeat(NotificationsLoops.Dispatcher).Returns(agora.AddSeconds(-5));
        heartbeat.LastBeat(NotificationsLoops.Avaliador).Returns(agora.AddSeconds(-30));
        heartbeat.LastBeat(NotificationsLoops.Coletor).Returns(agora.AddSeconds(-120));

        var sut = new NotificationsHostingHealthCheck(Opts(NotificationsHostingMode.Hosted), heartbeat);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Mode_Hosted_dispatcher_idle_alem_de_5x_intervalo_retorna_Unhealthy()
    {
        var heartbeat = Substitute.For<INotificationsLoopHeartbeat>();
        var agora = DateTimeOffset.UtcNow;
        // Janela = max(5 * 10s = 50s, floor 60s) = 60s. 5min idle = unhealthy.
        heartbeat.LastBeat(NotificationsLoops.Dispatcher).Returns(agora.AddMinutes(-5));
        heartbeat.LastBeat(NotificationsLoops.Avaliador).Returns(agora);
        heartbeat.LastBeat(NotificationsLoops.Coletor).Returns(agora);

        var sut = new NotificationsHostingHealthCheck(Opts(NotificationsHostingMode.Hosted), heartbeat);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("dispatcher")
            .And.NotContain("avaliador: idle")
            .And.NotContain("coletor: idle");
    }

    [Fact]
    public async Task Mode_Hosted_avaliador_idle_alem_da_janela_retorna_Unhealthy()
    {
        var heartbeat = Substitute.For<INotificationsLoopHeartbeat>();
        var agora = DateTimeOffset.UtcNow;
        // AvaliadorIntervalSeconds = 60s -> janela 5*60 = 300s. 10min idle = unhealthy.
        heartbeat.LastBeat(NotificationsLoops.Dispatcher).Returns(agora);
        heartbeat.LastBeat(NotificationsLoops.Avaliador).Returns(agora.AddMinutes(-10));
        heartbeat.LastBeat(NotificationsLoops.Coletor).Returns(agora);

        var sut = new NotificationsHostingHealthCheck(Opts(NotificationsHostingMode.Hosted), heartbeat);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("avaliador");
    }

    [Fact]
    public async Task Janela_dispatcher_respeita_floor_de_60_segundos_mesmo_com_intervalo_curto()
    {
        var heartbeat = Substitute.For<INotificationsLoopHeartbeat>();
        var agora = DateTimeOffset.UtcNow;
        // Intervalo curto (1s) — 5x = 5s. Floor de 60s previne flapping.
        heartbeat.LastBeat(NotificationsLoops.Dispatcher).Returns(agora.AddSeconds(-30));
        heartbeat.LastBeat(NotificationsLoops.Avaliador).Returns(agora);
        heartbeat.LastBeat(NotificationsLoops.Coletor).Returns(agora);

        var sut = new NotificationsHostingHealthCheck(
            Opts(NotificationsHostingMode.Hosted, dispatcherIntervalMs: 1_000),
            heartbeat);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        // 30s < floor 60s -> Healthy
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Health_data_inclui_idle_e_window_seconds()
    {
        var heartbeat = Substitute.For<INotificationsLoopHeartbeat>();
        var agora = DateTimeOffset.UtcNow;
        heartbeat.LastBeat(NotificationsLoops.Dispatcher).Returns(agora.AddSeconds(-15));
        heartbeat.LastBeat(NotificationsLoops.Avaliador).Returns(agora.AddSeconds(-15));
        heartbeat.LastBeat(NotificationsLoops.Coletor).Returns(agora.AddSeconds(-15));

        var sut = new NotificationsHostingHealthCheck(Opts(NotificationsHostingMode.Hosted), heartbeat);
        var result = await sut.CheckHealthAsync(new HealthCheckContext());

        result.Data.Should().ContainKey("dispatcher_idle_seconds")
            .And.ContainKey("dispatcher_window_seconds")
            .And.ContainKey("avaliador_idle_seconds")
            .And.ContainKey("coletor_idle_seconds");
    }
}
