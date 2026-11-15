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

    /// <summary>S68 (#2597): o molde padrão passa a citar a dona; sem nome, o texto é o mesmo de antes.</summary>
    [Fact]
    public void MoldePadraoDaEscaladaCitaADona()
    {
        ConfiguracaoAtendimento.MensagemEscaladaPadrao.Should().Contain(MoldeMensagemContato.Dona);
        MoldeMensagemContato.Preencher(ConfiguracaoAtendimento.MensagemEscaladaPadrao, "Maria")
            .Should().Be("Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.");
        MoldeMensagemContato.Preencher(ConfiguracaoAtendimento.MensagemEscaladaPadrao, "Maria", "Michele")
            .Should().Be("Vou encaminhar sua mensagem para Michele conferir e responder por aqui.");
    }
}
