using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>S34: o adaptador do WhatsApp só traduz a porta de canal para o cliente da Cloud API.</summary>
public class CanalWhatsAppTests
{
    private readonly IWhatsAppCloudClient _cloud = Substitute.For<IWhatsAppCloudClient>();

    [Fact]
    public async Task TextoDelegaAoCloudClientEDevolveWamid()
    {
        _cloud.EnviarTextoAsync("5511999990001", "Oi", null, Arg.Any<CancellationToken>())
            .Returns(new EnvioWhatsAppResult("wamid.1"));
        var canal = new CanalWhatsApp(_cloud);

        var id = await canal.EnviarTextoAsync("5511999990001", "Oi");

        canal.Canal.Should().Be(CanalConversa.WhatsApp);
        id.Should().Be("wamid.1");
    }

    [Fact]
    public async Task ModeloDelegaComoTemplate()
    {
        _cloud.EnviarTemplateAsync("5511999990001", "pedido_pago", "pt_BR",
                Arg.Is<IReadOnlyList<string>>(p => p.Count == 1 && p[0] == "#123"), null, Arg.Any<CancellationToken>())
            .Returns(new EnvioWhatsAppResult("wamid.2"));
        var canal = new CanalWhatsApp(_cloud);

        var id = await canal.EnviarModeloAsync("5511999990001", "pedido_pago", "pt_BR", ["#123"]);

        id.Should().Be("wamid.2");
    }
}
