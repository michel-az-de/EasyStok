using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Atendimento;

/// <summary>S42: resposta pronta com atalho e variáveis renderizadas no servidor.</summary>
public class RespostaProntaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriarNormalizaAtalhoETexto()
    {
        var resposta = RespostaPronta.Criar(Empresa, "  Boas-vindas ", " /Oi ", "  Olá, {nome}!  ", Agora);

        resposta.Titulo.Should().Be("Boas-vindas");
        resposta.Atalho.Should().Be("/oi");
        resposta.Texto.Should().Be("Olá, {nome}!");
        resposta.Arquivada.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("com espaco")]
    public void AtalhoInvalidoRecusa(string atalho)
    {
        var act = () => RespostaPronta.Criar(Empresa, "T", atalho, "texto", Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RenderSubstituiVariaveis()
    {
        var resposta = RespostaPronta.Criar(Empresa, "T", "/p", "Oi {nome}, pedido {pedido} às {faixa}.", Agora);

        var texto = resposta.Renderizar(new Dictionary<string, string?>
        {
            ["nome"] = "Ana", ["pedido"] = "AB12CD34", ["faixa"] = "30/09 às 18:00",
        });

        texto.Should().Be("Oi Ana, pedido AB12CD34 às 30/09 às 18:00.");
    }

    [Fact]
    public void RenderSemVariavelFalha()
    {
        var resposta = RespostaPronta.Criar(Empresa, "T", "/p", "Seu pedido {pedido} saiu.", Agora);

        var act = () => resposta.Renderizar(new Dictionary<string, string?> { ["nome"] = "Ana", ["pedido"] = null });

        act.Should().Throw<VariavelSemValorException>().Which.Variavel.Should().Be("pedido");
    }

    [Fact]
    public void RenderIgnoraChaveQueNaoEVariavel()
    {
        var texto = ModeloTextoAtendimento.Renderizar("Use {cupom} hoje", new Dictionary<string, string?>());
        texto.Should().Be("Use {cupom} hoje");
    }

    [Fact]
    public void ArquivarEEditar()
    {
        var resposta = RespostaPronta.Criar(Empresa, "T", "/p", "x", Agora);

        resposta.Editar("Novo", "/novo", "y", Agora.AddMinutes(1));
        resposta.Arquivar(Agora.AddMinutes(2));

        resposta.Atalho.Should().Be("/novo");
        resposta.Texto.Should().Be("y");
        resposta.Arquivada.Should().BeTrue();
    }
}

/// <summary>S42: regra automática por gatilho e a escolha do gatilho na entrada do cliente.</summary>
public class RegraAutomaticaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CriarExigeTextoEGatilhoValido()
    {
        var regra = RegraAutomatica.Criar(Empresa, GatilhoAutomacao.PosEntrega, "Chegou tudo certo, {nome}?", true, Agora);
        regra.Ligada.Should().BeTrue();

        ((Action)(() => RegraAutomatica.Criar(Empresa, (GatilhoAutomacao)99, "x", true, Agora)))
            .Should().Throw<RegraDeDominioVioladaException>();
        ((Action)(() => RegraAutomatica.Criar(Empresa, GatilhoAutomacao.PosEntrega, " ", true, Agora)))
            .Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData(true, false, GatilhoAutomacao.PrimeiroContato)]
    [InlineData(false, false, GatilhoAutomacao.ForaDoHorario)]
    [InlineData(false, true, GatilhoAutomacao.LojaFechada)]
    public void GatilhoDaEntradaEscolheSoUm(bool aberta, bool fechadaNaMao, GatilhoAutomacao esperado) =>
        RegraAutomatica.GatilhoDaPrimeiraEntrada(aberta, fechadaNaMao).Should().Be(esperado);
}
