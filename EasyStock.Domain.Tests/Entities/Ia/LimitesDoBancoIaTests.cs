using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Ia;

/// <summary>
/// #2967: o domínio recusa o que o banco recusaria ou arredondaria em silêncio (numeric(12,6) e numeric(4,3)),
/// e a URL do provedor não carrega segredo fora do cofre.
/// </summary>
public class LimitesDoBancoIaTests
{
    private static readonly DateTime Agora = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    private static ModeloIa Modelo(decimal? entrada = 0.28m, decimal fator = 0.5m) =>
        ModeloIa.Criar(Guid.NewGuid(), Guid.NewGuid(), "deepseek-chat", "DeepSeek", entrada, 0.42m, 0.028m, fator, Agora);

    private static ProvedorIa Provedor(string url = "https://api.deepseek.com/anthropic", decimal? orcamento = 20m) =>
        ProvedorIa.Criar(Guid.NewGuid(), "DeepSeek", url, AutenticacaoProvedorIa.XApiKey, orcamento, Agora);

    [Theory]
    [InlineData("0.0004")]
    [InlineData("0.5555")]
    public void FatorComMaisDeTresCasasEhRecusado(string fator)
    {
        var act = () => Modelo(fator: decimal.Parse(fator, System.Globalization.CultureInfo.InvariantCulture));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("1000000")]
    [InlineData("0.0000001")]
    public void PrecoForaDeNumeric12x6EhRecusado(string preco)
    {
        var act = () => Modelo(entrada: decimal.Parse(preco, System.Globalization.CultureInfo.InvariantCulture));
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void PrecoNoLimiteEhAceito()
    {
        Modelo(entrada: 999_999.999999m).PrecoEntrada.Should().Be(999_999.999999m);
    }

    [Fact]
    public void CustoRecusaTokensNegativos()
    {
        var act = () => Modelo().Custo(-1, 0, 0, false);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void OrcamentoForaDeNumeric12x6EhRecusado()
    {
        var act = () => Provedor(orcamento: 1_000_000m);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("https://user:senha@api.deepseek.com/anthropic")]
    [InlineData("https://api.deepseek.com/anthropic?token=x")]
    [InlineData("https://api.deepseek.com/anthropic#x")]
    public void UrlComSegredoEmbutidoEhRecusada(string url)
    {
        var act = () => Provedor(url: url);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void CustoDaChamadaArredondaParaSeisCasasComoOBanco()
    {
        var c = ChamadaIa.Registrar(Guid.NewGuid(), null, null, "deepseek-chat", FinalidadeIa.Atendimento,
            null, 10, 0, 0, 0, 0.0000028m, 1, DesfechoChamadaIa.Ok, Agora);
        c.CustoUsd.Should().Be(0.000003m);
    }

    [Fact]
    public void CustoDaChamadaForaDeNumeric12x6EhRecusado()
    {
        var act = () => ChamadaIa.Registrar(Guid.NewGuid(), null, null, "deepseek-chat", FinalidadeIa.Atendimento,
            null, 10, 0, 0, 0, 1_000_000m, 1, DesfechoChamadaIa.Ok, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}
