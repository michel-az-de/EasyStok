using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Polly;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>
/// #1102: o cliente da Cloud API do atendimento tem chave própria
/// (<c>Atendimento:WhatsApp:Cliente</c>), para ligar o atendimento sem trocar o provider de
/// notificações. Sem a chave nova, vale a antiga (<c>Notifications:WhatsApp:Provider</c>); e o
/// provider <c>meta</c> sempre leva o cliente real, porque as notificações enviam por ele.
/// </summary>
public class WhatsAppCloudClientRegistroTests
{
    [Theory]
    [InlineData("meta", "stub", true)]
    [InlineData("META", null, true)]
    [InlineData("stub", "meta", true)] // provider meta envia por este cliente: stub aqui engoliria as notificações
    [InlineData(null, "meta", true)]
    [InlineData("", "meta", true)]
    [InlineData(null, "stub", false)]
    [InlineData("stub", "stub", false)]
    [InlineData(null, null, false)]
    public void ClienteDoAtendimentoTemChavePropriaComFallbackNaDoProvider(
        string? cliente, string? provider, bool esperadoMeta)
    {
        WhatsAppCloudClientServiceCollectionExtensions.UsaClienteMeta(cliente, provider)
            .Should().Be(esperadoMeta);
    }

    [Fact]
    public void ClienteMetaComProviderStubLigaSoOAtendimento()
    {
        using var provider = Construir(new Dictionary<string, string?>
        {
            ["Atendimento:WhatsApp:Cliente"] = "meta",
            ["Notifications:WhatsApp:Provider"] = "stub",
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>()
            .Should().BeOfType<WhatsAppCloudClient>();
    }

    [Fact]
    public void SemChaveNovaSegueOProviderAntigo()
    {
        using var provider = Construir(new Dictionary<string, string?>
        {
            ["Notifications:WhatsApp:Provider"] = "stub",
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IWhatsAppCloudClient>()
            .Should().BeOfType<StubWhatsAppCloudClient>();
    }

    private static ServiceProvider Construir(Dictionary<string, string?> valores)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IRemetenteWhatsApp>());
        var pipelines = Substitute.For<ResiliencePipelineProvider<string>>();
        pipelines.GetPipeline(Arg.Any<string>()).Returns(ResiliencePipeline.Empty);
        services.AddSingleton(pipelines);
        services.AddEasyStockWhatsAppCloudClient(configuration);
        return services.BuildServiceProvider();
    }
}
