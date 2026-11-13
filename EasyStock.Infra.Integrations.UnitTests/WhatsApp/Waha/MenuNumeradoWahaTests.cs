using EasyStock.Infra.Integrations.WhatsApp.Waha;
using FluentAssertions;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp.Waha;

public class MenuNumeradoWahaTests
{
    private static readonly MenuNumeradoWaha.Opcao[] Opcoes =
    [
        new("acao:avaliacao:positiva:abc", "Gostei"),
        new("acao:avaliacao:negativa:abc", "Não gostei"),
    ];

    [Theory]
    [InlineData("1", "acao:avaliacao:positiva:abc")]
    [InlineData(" 2 ", "acao:avaliacao:negativa:abc")]
    [InlineData("2.", "acao:avaliacao:negativa:abc")]
    [InlineData("2)", "acao:avaliacao:negativa:abc")]
    [InlineData("opção 2", "acao:avaliacao:negativa:abc")]
    [InlineData("Opcao: 1", "acao:avaliacao:positiva:abc")]
    [InlineData("2️⃣", "acao:avaliacao:negativa:abc")]
    [InlineData("nao gostei", "acao:avaliacao:negativa:abc")]
    [InlineData("GOSTEI!", "acao:avaliacao:positiva:abc")]
    public void EntendeONumeroOuOTituloDaOpcao(string texto, string idEsperado) =>
        MenuNumeradoWaha.Interpretar(texto, Opcoes)!.Id.Should().Be(idEsperado);

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("quero 2 bolos")]
    [InlineData("gostei muito")]
    public void TextoQueNaoEscolheOpcaoSegueComoTexto(string texto) =>
        MenuNumeradoWaha.Interpretar(texto, Opcoes).Should().BeNull();

    [Fact]
    public async Task EscolhaValeUmaVezSo()
    {
        var cache = WahaFalso.Cache();
        var menus = WahaFalso.Menus(cache, WahaFalso.Opcoes());
        await menus.GuardarAsync("5541988887777@c.us", [("a", "Sim"), ("b", "Não")], CancellationToken.None);

        var primeira = await menus.EscolhaAsync("5541988887777", "1", CancellationToken.None);
        var segunda = await menus.EscolhaAsync("5541988887777", "1", CancellationToken.None);

        primeira!.Id.Should().Be("a");
        segunda.Should().BeNull("o menu respondido some; um \"1\" depois é texto de novo");
    }

    [Fact]
    public async Task RespostaQueNaoEscolheMantemOMenu()
    {
        var cache = WahaFalso.Cache();
        var menus = WahaFalso.Menus(cache, WahaFalso.Opcoes());
        await menus.GuardarAsync("5541988887777", [("a", "Sim"), ("b", "Não")], CancellationToken.None);

        (await menus.EscolhaAsync("5541988887777", "como assim?", CancellationToken.None)).Should().BeNull();
        (await menus.EscolhaAsync("5541988887777", "2", CancellationToken.None))!.Id.Should().Be("b");
    }
}
