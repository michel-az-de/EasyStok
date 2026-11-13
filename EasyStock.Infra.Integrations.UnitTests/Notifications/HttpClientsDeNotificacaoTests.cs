using EasyStock.Infra.Notifications.DependencyInjection;
using EasyStock.Infra.Notifications.Push;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Integrations.UnitTests.Notifications;

/// <summary>#2401: o envio ao provedor travava até 100 s (padrão do HttpClient) com o provedor lento.</summary>
public class HttpClientsDeNotificacaoTests
{
    [Theory]
    [InlineData("TwilioSms")]
    [InlineData("MetaWhatsApp")]
    [InlineData(WebPushCanal.NomeHttpClient)]
    public void HttpClientDeEnvio_TemTimeoutDe15Segundos(string nome)
    {
        var services = new ServiceCollection();
        services.AddNotificationsInfra(new ConfigurationBuilder().Build());
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IHttpClientFactory>().CreateClient(nome).Timeout.Should().Be(TimeSpan.FromSeconds(15));
    }
}
