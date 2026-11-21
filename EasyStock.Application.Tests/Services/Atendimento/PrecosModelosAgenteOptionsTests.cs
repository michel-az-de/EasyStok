using EasyStock.Application.Services.Atendimento;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>S67 (#2534): custo estimado do turno pela tabela de preço por milhão de tokens.</summary>
public class PrecosModelosAgenteOptionsTests
{
    private static PrecosModelosAgenteOptions Tabela()
    {
        var tabela = new PrecosModelosAgenteOptions();
        tabela.Modelos["claude-sonnet-5"] = new PrecoModeloAgente(3m, 15m, 0.3m);
        tabela.Modelos["claude-sonnet-5-mini"] = new PrecoModeloAgente(1m, 5m, 0.1m);
        return tabela;
    }

    [Fact]
    public void CustoEhTokensVezesPrecoPorMilhao() =>
        // 1M de entrada a 3, 100 mil de saída a 15 e 200 mil de cache lido a 0,30: 3 + 1,5 + 0,06.
        Tabela().Estimar("claude-sonnet-5", 1_000_000, 100_000, 200_000).Should().Be(4.56m);

    [Fact]
    public void VersaoDatadaUsaOPrecoDoNomeMaisEspecifico()
    {
        Tabela().Estimar("claude-sonnet-5-20261101", 1_000_000, 0, 0).Should().Be(3m);
        Tabela().Estimar("claude-sonnet-5-mini-20261101", 1_000_000, 0, 0).Should().Be(1m);
    }

    [Theory]
    [InlineData("gpt-5")]
    [InlineData("desconhecido")]
    [InlineData(null)]
    public void ModeloSemPrecoDaCustoNulo(string? modelo) =>
        Tabela().Estimar(modelo, 1_000, 100, 0).Should().BeNull();

    [Fact]
    public void LeATabelaDaConfiguracao()
    {
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Anthropic:Precos:claude-sonnet-5:Entrada"] = "3",
            ["Anthropic:Precos:claude-sonnet-5:Saida"] = "15",
            ["Anthropic:Precos:claude-sonnet-5:CacheLido"] = "0.3",
            ["Anthropic:Precos:sem-saida:Entrada"] = "1",
        }).Build();

        var tabela = new PrecosModelosAgenteOptions();
        tabela.Ler(configuracao);

        tabela.Modelos.Should().ContainKey("claude-sonnet-5").WhoseValue.Should().Be(new PrecoModeloAgente(3m, 15m, 0.3m));
        tabela.Modelos.Should().NotContainKey("sem-saida", "preço pela metade daria custo errado; melhor nulo");
    }
}
