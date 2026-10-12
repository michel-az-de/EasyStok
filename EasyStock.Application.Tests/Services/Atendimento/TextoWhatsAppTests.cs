using EasyStock.Application.Services.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#1330: o agente não manda travessão nem meia-risca ao cliente, obedecendo ou não o prompt.</summary>
public class TextoWhatsAppTests
{
    [Theory]
    [InlineData("Ravioli de carne 400g — R$ 22,50", "Ravioli de carne 400g, R$ 22,50")]
    [InlineData("Temos ravioli — e nhoque — hoje.", "Temos ravioli, e nhoque, hoje.")]
    [InlineData("Oi! — Tudo bem?", "Oi! Tudo bem?")]
    [InlineData("— Ravioli\n— Nhoque", "- Ravioli\n- Nhoque")]
    [InlineData("Entregamos de seg—sáb", "Entregamos de seg-sáb")]
    [InlineData("das 10–12h", "das 10-12h")]
    [InlineData("Massa fresca – R$ 13,90", "Massa fresca, R$ 13,90")]
    [InlineData("Obrigado —", "Obrigado")]
    [InlineData("Sem nada para trocar.", "Sem nada para trocar.")]
    public void TrocaTravessaoPorPontuacaoSimples(string entrada, string esperado) =>
        TextoWhatsApp.SemTravessao(entrada).Should().Be(esperado);

    [Fact]
    public void TextoVazioPassaIgual() => TextoWhatsApp.SemTravessao("").Should().BeEmpty();
}
