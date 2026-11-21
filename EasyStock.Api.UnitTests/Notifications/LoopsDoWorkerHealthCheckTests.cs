using EasyStock.Infra.Notifications.Hosting;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EasyStock.TestHelpers;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #2536: os loops do Worker fora do motor de notificações (outbox, agendamento, relatórios...) também alimentam o
/// arquivo de vida. Loop atrasado além de max(5x intervalo, 5 min) deixa o check Unhealthy; loop desligado não se
/// registra e não conta.
/// </summary>
public sealed class LoopsDoWorkerHealthCheckTests
{
    private readonly FakeTimeProvider _relogio = new(DateTimeOffset.Parse("2026-11-14T03:00:00Z"));

    private LoopsDoWorker Monitor() => new(_relogio);

    private static Task<HealthCheckResult> Checar(LoopsDoWorker monitor) =>
        new LoopsDoWorkerHealthCheck(monitor).CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Sem_loop_registrado_fica_saudavel()
    {
        (await Checar(Monitor())).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Loop_que_bate_dentro_da_janela_fica_saudavel()
    {
        var monitor = Monitor();
        monitor.Iniciar("outbox", TimeSpan.FromSeconds(30));

        _relogio.Advance(TimeSpan.FromMinutes(4));
        monitor.Bater("outbox");
        _relogio.Advance(TimeSpan.FromMinutes(4));

        (await Checar(monitor)).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Loop_parado_alem_do_piso_de_5_minutos_fica_unhealthy()
    {
        var monitor = Monitor();
        monitor.Iniciar("outbox", TimeSpan.FromSeconds(30));

        _relogio.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        var resultado = await Checar(monitor);
        resultado.Status.Should().Be(HealthStatus.Unhealthy);
        resultado.Description.Should().Contain("outbox");
    }

    [Fact]
    public async Task Janela_de_loop_lento_e_5_vezes_o_intervalo()
    {
        var monitor = Monitor();
        monitor.Iniciar("limpeza-tokens", TimeSpan.FromHours(6));

        _relogio.Advance(TimeSpan.FromHours(29));
        (await Checar(monitor)).Status.Should().Be(HealthStatus.Healthy);

        _relogio.Advance(TimeSpan.FromHours(2));
        (await Checar(monitor)).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void Check_vai_para_o_arquivo_de_vida_pela_tag_dispatcher()
    {
        LoopsDoWorkerHealthCheck.Tags.Should().Contain("dispatcher");
    }
}
