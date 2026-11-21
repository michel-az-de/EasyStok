using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Ia;

public class ModeloIaTests
{
    private static readonly DateTime Agora = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    private static ModeloIa Criar(decimal? entrada = 0.28m, decimal? saida = 0.42m, decimal? cache = 0.028m, decimal fator = 0.5m) =>
        ModeloIa.Criar(Guid.NewGuid(), Guid.NewGuid(), "deepseek-chat", "DeepSeek Flash", entrada, saida, cache, fator, Agora);

    [Fact]
    public void NasceDesligadoENaoPadrao()
    {
        var m = Criar();
        m.Ligado.Should().BeFalse();
        m.Padrao.Should().BeFalse();
    }

    [Fact]
    public void RecusaPrecoNegativo()
    {
        var act = () => Criar(entrada: -0.1m);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.01)]
    public void FatorForaPicoFicaEntreZeroEUm(double fator)
    {
        var act = () => Criar(fator: (decimal)fator);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void ExigeEmpresaProvedorEIdExterno()
    {
        var semProvedor = () => ModeloIa.Criar(Guid.NewGuid(), Guid.Empty, "x", "x", null, null, null, 1m, Agora);
        var semId = () => ModeloIa.Criar(Guid.NewGuid(), Guid.NewGuid(), " ", "x", null, null, null, 1m, Agora);
        semProvedor.Should().Throw<RegraDeDominioVioladaException>();
        semId.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void PadraoPrecisaEstarLigado()
    {
        var m = Criar();

        var desligado = () => m.TornarPadrao(Agora);
        desligado.Should().Throw<RegraDeDominioVioladaException>();

        m.Ligar(Agora);
        m.TornarPadrao(Agora);
        m.Padrao.Should().BeTrue();

        var desligarPadrao = () => m.Desligar(Agora);
        desligarPadrao.Should().Throw<RegraDeDominioVioladaException>();

        m.DeixarDeSerPadrao(Agora);
        m.Desligar(Agora);
        m.Ligado.Should().BeFalse();
    }

    [Fact]
    public void CustoNoPicoUsaPrecoCheio()
    {
        // 1M entrada * 0,28 + 1M saida * 0,42 + 1M cache * 0,028
        Criar().Custo(1_000_000, 1_000_000, 1_000_000, foraDoPico: false).Should().Be(0.728m);
    }

    [Fact]
    public void CustoForaDoPicoAplicaFator()
    {
        Criar().Custo(1_000_000, 1_000_000, 1_000_000, foraDoPico: true).Should().Be(0.364m);
    }

    [Fact]
    public void CustoNuloSemPreco()
    {
        Criar(entrada: null).Custo(10, 10, 0, foraDoPico: false).Should().BeNull();
    }
}
