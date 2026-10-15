using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>S34 (ADR-0051): o envio escolhe o adaptador pelo canal da conversa.</summary>
public class ResolvedorCanalTests
{
    [Fact]
    public void DevolveAdaptadorDoCanal()
    {
        var whats = Substitute.For<ICanalMensageria>();
        whats.Canal.Returns(CanalConversa.WhatsApp);
        var resolvedor = new ResolvedorCanal([whats]);

        resolvedor.Obter(CanalConversa.WhatsApp).Should().BeSameAs(whats);
    }

    [Fact]
    public void CanalSemAdaptadorLanca()
    {
        // Instagram declarado no domínio, mas sem adaptador registrado (entra na S35).
        var whats = Substitute.For<ICanalMensageria>();
        whats.Canal.Returns(CanalConversa.WhatsApp);
        var resolvedor = new ResolvedorCanal([whats]);

        var act = () => resolvedor.Obter(CanalConversa.Instagram);

        act.Should().Throw<CanalNaoSuportadoException>().Which.Canal.Should().Be(CanalConversa.Instagram);
    }
}
