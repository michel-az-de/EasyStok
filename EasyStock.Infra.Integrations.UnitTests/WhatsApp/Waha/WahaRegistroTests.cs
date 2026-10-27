using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.WhatsApp;
using EasyStock.Infra.Integrations.WhatsApp.Waha;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp.Waha;

/// <summary>
/// #2072: <c>Atendimento:WhatsApp:Cliente=waha</c> liga a ponte no atendimento e vence a Meta, mesmo com o provider de
/// notificações em <c>meta</c>. O número de plataforma segue na Meta.
/// </summary>
public class WahaRegistroTests
{
    private static ServiceProvider Construir(Dictionary<string, string?> valores)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddSingleton(Substitute.For<IRemetenteWhatsApp>());
        var pipelines = Substitute.For<ResiliencePipelineProvider<string>>();
        pipelines.GetPipeline(Arg.Any<string>()).Returns(ResiliencePipeline.Empty);
        services.AddSingleton(pipelines);
        services.AddEasyStockWhatsAppCloudClient(configuration);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> ComPonte() => new()
    {
        ["Atendimento:WhatsApp:Cliente"] = "waha",
        ["Notifications:WhatsApp:Provider"] = "meta",
        ["Atendimento:WhatsApp:Waha:BaseUrl"] = "http://ez-waha:3000",
        ["Atendimento:WhatsApp:Waha:ApiKey"] = "chave",
        ["Atendimento:WhatsApp:Waha:PhoneNumberId"] = "5541999990000",
    };

    [Fact]
    public void PonteLigadaVenceAMetaNoAtendimento()
    {
        using var provider = Construir(ComPonte());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>().Should().BeOfType<WahaWhatsAppClient>();
        scope.ServiceProvider.GetRequiredService<ICanalMensageria>().Should().BeOfType<CanalWhatsAppPonte>()
            .Which.Should().BeAssignableTo<ICanalSemModeloAprovado>();
        scope.ServiceProvider.GetRequiredService<ICatalogoModelosAtendimento>().Should().BeOfType<CatalogoModelosIndisponivel>();
        scope.ServiceProvider.GetRequiredService<IOptions<WahaOptions>>().Value.Ligada.Should().BeTrue();
    }

    [Fact]
    public void PlataformaContinuaNaMeta()
    {
        using var provider = Construir(ComPonte());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<EasyStock.Application.Ports.Output.Notifications.IClienteWhatsAppPlataforma>()
            .Should().BeOfType<WhatsAppCloudClient>();
    }

    [Fact]
    public void ClienteHttpDoWahaLevaEnderecoEChave()
    {
        using var provider = Construir(ComPonte());

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WahaWhatsAppClient));

        http.BaseAddress.Should().Be(new Uri("http://ez-waha:3000/"));
        http.DefaultRequestHeaders.GetValues("X-Api-Key").Should().Equal("chave");
    }

    [Fact]
    public void SemPonteOWhatsAppSegueComoAntes()
    {
        using var provider = Construir(new Dictionary<string, string?> { ["Atendimento:WhatsApp:Cliente"] = "meta" });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>().Should().BeOfType<WhatsAppCloudClient>();
        scope.ServiceProvider.GetRequiredService<ICanalMensageria>().Should().BeOfType<CanalWhatsApp>();
        scope.ServiceProvider.GetRequiredService<IOptions<WahaOptions>>().Value.Ligada.Should().BeFalse();
        scope.ServiceProvider.GetRequiredService<TradutorEventoWaha>().Should().NotBeNull("o webhook do WAHA resolve sempre e responde 404");
    }

    [Fact]
    public void WahaEWppConnectLigadosJuntosNaoSobem()
    {
        var valores = ComPonte();
        valores["Atendimento:WppConnect:Habilitado"] = "true";

        var acao = () => Construir(valores);

        acao.Should().Throw<InvalidOperationException>().WithMessage("*only one WhatsApp bridge*");
    }

    [Fact]
    public void WppConnectSozinhoNaoLigaOWaha()
    {
        using var provider = Construir(new Dictionary<string, string?>
        {
            ["Atendimento:WppConnect:Habilitado"] = "true",
            ["Notifications:WhatsApp:Provider"] = "meta",
        });

        provider.GetRequiredService<IOptions<WahaOptions>>().Value.Ligada.Should().BeFalse();
    }

    [Theory]
    [InlineData("waha", true)]
    [InlineData(" WAHA ", true)]
    [InlineData("meta", false)]
    [InlineData(null, false)]
    public void ChaveDoClienteLigaAPonte(string? cliente, bool ligada) =>
        WhatsAppCloudClientServiceCollectionExtensions.UsaPonteWaha(cliente).Should().Be(ligada);
}
