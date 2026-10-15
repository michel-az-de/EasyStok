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
    public void Cada_coletor_fica_registrado_uma_vez_mesmo_chamando_o_registro_duas_vezes()
    {
        var services = new ServiceCollection();

        services.AddEasyStockNotificationsRepositories();
        services.AddEasyStockNotificationsRepositories();

        var coletores = services
            .Where(d => d.ServiceType == typeof(IColetorEventoNotificacao))
            .Select(d => d.ImplementationType)
            .ToList();

        coletores.Should().NotBeEmpty();
        coletores.Should().OnlyHaveUniqueItems("dois descritores fazem o mesmo coletor rodar duas vezes por rodada");
    }
}
