using EasyStock.Api.DependencyInjection;
using EasyStock.Application.Services.Notifications;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Infra.Postgre.Notifications;
using EasyStock.Infra.Postgre.Notifications.Maintenance;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// N1, um host só: o Worker é o único que roda os loops do motor. A API ignora <c>Mode=Hosted</c> da configuração
/// (antes o padrão era Hosted e, com API e Worker de pé, os dois avaliavam o mesmo evento).
/// </summary>
public class NotificationsModuleExtensionsTests
{
    private static readonly Type[] TiposDosLoops =
    [
        typeof(DispatcherLoopHostedService), typeof(AvaliadorLoopHostedService), typeof(ColetorLoopHostedService),
        typeof(PostgresOutboxSignaler), typeof(AnonimizarLogsAntigosService),
    ];

    private static ServiceCollection Registrar(Dictionary<string, string?> configuracao)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configuracao).Build();
        var services = new ServiceCollection();
        services.AddEasyStockNotificationsModule(config);
        return services;
    }

    [Theory]
    [InlineData("Hosted")]
    [InlineData(null)]
    public void Api_nunca_registra_os_loops_mesmo_com_Mode_Hosted_na_configuracao(string? modo)
    {
        var configuracao = new Dictionary<string, string?>();
        if (modo is not null) configuracao["Notifications:Hosting:Mode"] = modo;

        var services = Registrar(configuracao);

        var tiposRegistrados = services
            .SelectMany(d => new[] { d.ServiceType, d.ImplementationType })
            .OfType<Type>()
            .ToHashSet();
        foreach (var tipo in TiposDosLoops)
            tiposRegistrados.Should().NotContain(tipo, $"a API não hospeda {tipo.Name}");
        tiposRegistrados.Should().NotContain(typeof(IOutboxSignaler), "sem loop não há sinalizador");
    }

    [Fact]
    public void Api_com_Hosted_na_configuracao_reporta_Disabled_para_o_health_dispatcher()
    {
        var services = Registrar(new() { ["Notifications:Hosting:Mode"] = "Hosted" });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<NotificationsHostingOptions>>().Value.Mode
            .Should().Be(NotificationsHostingMode.Disabled, "/health/dispatcher diz disabled neste host");
    }
}
