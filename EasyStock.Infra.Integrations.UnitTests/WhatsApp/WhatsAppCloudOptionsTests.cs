using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.WhatsApp;
using EasyStock.Infra.Notifications.Options;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>N6: a versão da Graph mora num lugar só; a URL base deriva dela e só a homologação a sobrescreve.</summary>
public class WhatsAppCloudOptionsTests
{
    [Fact]
    public void AsDuasOptionsNascemNaMesmaVersao()
    {
        var cliente = new WhatsAppCloudOptions();
        var notificacoes = new MetaCloudWhatsAppOptions();

        cliente.ApiVersion.Should().Be(VersaoGraphApi.Padrao);
        notificacoes.ApiVersion.Should().Be(VersaoGraphApi.Padrao);
        VersaoGraphApi.Padrao.Should().Be("v26.0");
    }

    [Fact]
    public void BaseUrlDerivaDaVersao()
    {
        new WhatsAppCloudOptions { ApiVersion = "v25.0" }.BaseUrlEfetiva
            .Should().Be("https://graph.facebook.com/v25.0");
        new MetaCloudWhatsAppOptions { ApiVersion = "v25.0" }.BaseUrlEfetiva
            .Should().Be("https://graph.facebook.com/v25.0");

        // Override só para a homologação.
        new WhatsAppCloudOptions { BaseUrl = "http://fake:18620/v26.0" }.BaseUrlEfetiva
            .Should().Be("http://fake:18620/v26.0");
    }

    [Fact]
    public async Task ClienteUsaABaseDaVersaoConfigurada()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Meta:ApiVersion"] = "v25.0",
            ["Notifications:WhatsApp:Meta:AccessToken"] = "t",
            ["Notifications:WhatsApp:Plataforma:PhoneNumberId"] = "7770009999"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEasyStockIntegrationResilience();
        services.AddScoped<EasyStock.Application.Ports.Output.Atendimento.IRemetenteWhatsApp>(
            _ => NSubstitute.Substitute.For<EasyStock.Application.Ports.Output.Atendimento.IRemetenteWhatsApp>());
        services.AddEasyStockWhatsAppCloudClient(config);
        await using var sp = services.BuildServiceProvider();

        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("whatsapp-cloud");

        http.BaseAddress!.ToString().Should().Be("https://graph.facebook.com/v25.0/");
        sp.GetRequiredService<IOptions<WhatsAppCloudOptions>>().Value.PhoneNumberIdPlataforma.Should().Be("7770009999");
    }
}
