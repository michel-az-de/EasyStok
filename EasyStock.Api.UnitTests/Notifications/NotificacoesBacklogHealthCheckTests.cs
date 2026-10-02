using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Notifications.Hosting;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// N1, ver e avisar: o backlog do motor vira health check. O que o heartbeat não vê (loop vivo que não envia, envio que
/// só simula, mensagem presa no limbo) aparece aqui, com limites em <c>Notifications:Health:*</c>.
/// </summary>
public class NotificacoesBacklogHealthCheckTests
{
    private static BacklogNotificacoes Backlog(
        TimeSpan? pendenteElegivel = null, TimeSpan? eventoPendente = null, int emEnvioAlemDoLease = 0,
        int falhado = 0, int simulado = 0, int indeterminado = 0, int expirado = 0, int processadoSemOutbox = 0) =>
        new(pendenteElegivel, eventoPendente, emEnvioAlemDoLease, falhado, simulado, indeterminado, expirado, processadoSemOutbox);

    private static NotificacoesBacklogHealthCheck NovoCheck(
        BacklogNotificacoes backlog, string ambiente = "Production", Dictionary<string, string?>? configuracao = null,
        NotificacoesHealthOptions? opcoes = null)
    {
        var medidor = Substitute.For<IBacklogNotificacoes>();
        medidor.MedirAsync(Arg.Any<CancellationToken>()).Returns(backlog);
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(ambiente);
        // Providers reais por padrão: os testes de Simulado e de backlog não querem o aviso de provider de teste.
        var config = new ConfigurationBuilder().AddInMemoryCollection(configuracao ?? new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Provider"] = "meta",
            ["Notifications:Sms:Provider"] = "twilio",
        }).Build();
        var services = new ServiceCollection().AddSingleton(Substitute.For<IEmailService>()).BuildServiceProvider();
        return new NotificacoesBacklogHealthCheck(
            medidor, Options.Create(opcoes ?? new NotificacoesHealthOptions()), env, config, services);
    }

    private static Task<HealthCheckResult> Rodar(NotificacoesBacklogHealthCheck check) =>
        check.CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Sem_backlog_e_Healthy()
    {
        var resultado = await Rodar(NovoCheck(Backlog()));

        resultado.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Pendente_elegivel_acima_do_limite_deixa_Unhealthy()
    {
        var resultado = await Rodar(NovoCheck(Backlog(pendenteElegivel: TimeSpan.FromMinutes(6))));

        resultado.Status.Should().Be(HealthStatus.Unhealthy);
        resultado.Description.Should().Contain("Pendente elegível");
        resultado.Data.Should().ContainKey("pendente_elegivel_idade_segundos");
    }

    [Fact]
    public async Task Pendente_elegivel_dentro_do_limite_nao_alarma()
    {
        var resultado = await Rodar(NovoCheck(Backlog(pendenteElegivel: TimeSpan.FromMinutes(2))));

        resultado.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task EmEnvio_alem_do_lease_deixa_Unhealthy_porque_o_loop_nao_fecha_o_que_reserva()
    {
        var resultado = await Rodar(NovoCheck(Backlog(emEnvioAlemDoLease: 1)));

        resultado.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Simulado_em_Production_deixa_Degraded()
    {
        var resultado = await Rodar(NovoCheck(Backlog(simulado: 1)));

        resultado.Status.Should().Be(HealthStatus.Degraded);
        resultado.Description.Should().Contain("Simulado");
    }

    [Fact]
    public async Task Fora_de_Production_Simulado_nao_degrada()
    {
        var resultado = await Rodar(NovoCheck(Backlog(simulado: 3), ambiente: "Development"));

        resultado.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Indeterminado_na_ultima_hora_deixa_Degraded_em_qualquer_ambiente()
    {
        var resultado = await Rodar(NovoCheck(Backlog(indeterminado: 1), ambiente: "Development"));

        resultado.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Falhado_por_hora_so_degrada_a_partir_do_limite()
    {
        (await Rodar(NovoCheck(Backlog(falhado: 4)))).Status.Should().Be(HealthStatus.Healthy);
        (await Rodar(NovoCheck(Backlog(falhado: 5)))).Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Provider_stub_em_Production_deixa_Degraded_mesmo_sem_mensagem_simulada()
    {
        var resultado = await Rodar(NovoCheck(Backlog(), configuracao: new()
        {
            ["Notifications:WhatsApp:Provider"] = "stub",
            ["Notifications:Sms:Provider"] = "twilio",
        }));

        resultado.Status.Should().Be(HealthStatus.Degraded);
        resultado.Description.Should().Contain("WhatsApp").And.Contain("stub");
    }

    [Fact]
    public async Task Provider_stub_fora_de_Production_e_o_esperado()
    {
        var resultado = await Rodar(NovoCheck(Backlog(), ambiente: "Development", configuracao: new()));

        resultado.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Limites_vem_da_configuracao()
    {
        var opcoes = new NotificacoesHealthOptions { PendenteElegivelMaxMinutos = 1, FalhadoPorHoraMax = 1 };

        (await Rodar(NovoCheck(Backlog(pendenteElegivel: TimeSpan.FromMinutes(2)), opcoes: opcoes))).Status
            .Should().Be(HealthStatus.Unhealthy);
        (await Rodar(NovoCheck(Backlog(falhado: 1), opcoes: opcoes))).Status.Should().Be(HealthStatus.Degraded);
    }
}
