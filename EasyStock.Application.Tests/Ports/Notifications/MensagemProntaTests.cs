using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.Ports.Notifications;

/// <summary>S09: metadados do envio (template da Meta e parâmetros) são aditivos e opcionais.</summary>
public class MensagemProntaTests
{
    [Fact]
    public void MetadadosOpcionais()
    {
        var semMetadados = new MensagemPronta(Guid.NewGuid(), Guid.NewGuid(), "5511999990001", "a", "b",
            CanalNotificacao.WhatsApp, CategoriaConteudoNotificacao.Transacional);
        var comMetadados = semMetadados with
        {
            Metadados = new Dictionary<string, string> { ["template"] = "pedido_pago", ["param1"] = "#123" }
        };

        semMetadados.Metadados.Should().BeNull();
        comMetadados.Metadados!["template"].Should().Be("pedido_pago");
        comMetadados.Metadados["param1"].Should().Be("#123");
    }
}
