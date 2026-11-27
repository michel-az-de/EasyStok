using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

public class MoldeMensagemContatoTests
{
    [Theory]
    [InlineData("Oi, {nome}! Eu sou o robô.", "Joao", "Oi, Joao! Eu sou o robô.")]
    [InlineData("Oi, {Nome}! Eu sou o robô.", "Joao", "Oi, Joao! Eu sou o robô.")]
    [InlineData("Oi, {nome}! Eu sou o robô.", null, "Oi! Eu sou o robô.")]
    [InlineData("Oi {nome}! Eu sou o robô.", " ", "Oi! Eu sou o robô.")]
    [InlineData("{nome}, vou passar para a equipe.", null, "Vou passar para a equipe.")]
    [InlineData("Oi, {nome}, tudo bem?", null, "Oi, tudo bem?")]
    [InlineData("Oi, {NOME}!", null, "Oi!")]
    public void PreencheOuRemoveONomeSemDeixarPontuacaoSolta(string molde, string? nome, string esperado) =>
        MoldeMensagemContato.Preencher(molde, nome).Should().Be(esperado);

    /// <summary>S68 (#2597): <c>{dona}</c> vira o nome da dona; sem nome configurado, "a equipe".</summary>
    [Theory]
    [InlineData("Vou encaminhar para {dona} conferir.", "Michele", "Vou encaminhar para Michele conferir.")]
    [InlineData("Vou encaminhar para {Dona} conferir.", null, "Vou encaminhar para a equipe conferir.")]
    [InlineData("Vou encaminhar para {dona} conferir.", " ", "Vou encaminhar para a equipe conferir.")]
    [InlineData("{dona} já te responde, {nome}.", null, "A equipe já te responde, Maria.")]
    public void PreencheADonaOuFalaDaEquipe(string molde, string? dona, string esperado) =>
        MoldeMensagemContato.Preencher(molde, molde.Contains("{nome}") ? "Maria" : null, dona).Should().Be(esperado);

    /// <summary>#3379: a escalada padrão fala como a atendente, sem anunciar repasse à dona nem encaminhamento.</summary>
    [Fact]
    public void MoldePadraoDaEscaladaNaoAnunciaRepasse()
    {
        var texto = MoldeMensagemContato.Preencher(ConfiguracaoAtendimento.MensagemEscaladaHumana, "Maria", "Michele");
        texto.Should().Be("Só um instante, já te confirmo isso por aqui.");
        texto.Should().NotContain("Michele").And.NotContain("encaminh");
        MoldeMensagemContato.Preencher(ConfiguracaoAtendimento.MensagemEscaladaHumana, null)
            .Should().Be("Só um instante, já te confirmo isso por aqui.");
    }

    /// <summary>Revisão da #2618: com o nome do cliente, o molde sai como a loja escreveu, como antes da S68.</summary>
    [Fact]
    public void ComNomeOMoldeNaoGanhaMaiusculaForcada() =>
        MoldeMensagemContato.Preencher("vou passar para a {dona}, {nome}.", "Maria", "Michele")
            .Should().Be("vou passar para a Michele, Maria.");
}
