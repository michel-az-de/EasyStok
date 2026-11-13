using EasyStock.Infra.Integrations.WhatsApp.Waha;
using FluentAssertions;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp.Waha;

public class WahaIdsTests
{
    [Theory]
    [InlineData("false_5541988887777@c.us_3EB0ABC", false, "5541988887777@c.us", "3EB0ABC")]
    [InlineData("true_123456789012345@lid_ABC", true, "123456789012345@lid", "ABC")]
    [InlineData("false_120363@g.us_ABC_5541988887777@c.us", false, "120363@g.us", "ABC")]
    public void LeOIdSerializado(string serializado, bool deMim, string conversa, string id)
    {
        var lido = WahaIds.LerIdMensagem(serializado)!;

        lido.DeMim.Should().Be(deMim);
        lido.Conversa.Should().Be(conversa);
        lido.Id.Should().Be(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wamid.HBgM")]
    [InlineData("talvez_5541@c.us_ABC")]
    [InlineData("false__ABC")]
    public void IdForaDoFormatoEhNulo(string? serializado) => WahaIds.LerIdMensagem(serializado).Should().BeNull();

    [Theory]
    [InlineData("5541988887777@c.us", "5541988887777")]
    [InlineData("5541988887777:12@s.whatsapp.net", "5541988887777")]
    [InlineData("123456789012345@lid", "123456789012345")]
    public void UsuarioSoComDigitos(string jid, string usuario) => WahaIds.Usuario(jid).Should().Be(usuario);

    [Theory]
    [InlineData("5541988887777@c.us", true)]
    [InlineData("123@lid", true)]
    [InlineData("120363@g.us", false)]
    [InlineData("status@broadcast", false)]
    [InlineData("123@newsletter", false)]
    public void SoConversaComPessoa(string jid, bool individual) => WahaIds.EhConversaIndividual(jid).Should().Be(individual);
}
