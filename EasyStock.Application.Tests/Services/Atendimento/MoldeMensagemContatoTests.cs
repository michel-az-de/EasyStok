using EasyStock.Application.Services.Atendimento;

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
}
