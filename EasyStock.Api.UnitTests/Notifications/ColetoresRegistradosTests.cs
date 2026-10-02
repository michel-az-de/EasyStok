using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Postgre.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// N1: o Worker chamava <c>AddEasyStockNotificationsRepositories</c> depois de <c>AddEasyStockPostgreInfrastructure</c>,
/// que já o chama, e o coletor rodava duas vezes por rodada. O registro passa a ser idempotente.
/// </summary>
public class ColetoresRegistradosTests
{
    [Fact]
    public void Coletor_fica_registrado_uma_vez_mesmo_chamando_o_registro_duas_vezes()
    {
        var services = new ServiceCollection();

        services.AddEasyStockNotificationsRepositories();
        services.AddEasyStockNotificationsRepositories();

        services.Count(d => d.ServiceType == typeof(IColetorEventoNotificacao))
            .Should().Be(1, "dois descritores fazem o mesmo coletor rodar duas vezes por rodada");
    }
}
