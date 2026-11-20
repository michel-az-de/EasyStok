using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Domain.Exceptions;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Entities.Ia;

public class ProvedorIaTests
{
    private static readonly DateTime Agora = new(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

    private static ProvedorIa Criar(decimal? orcamento = 20m, string url = "https://api.deepseek.com/anthropic") =>
        ProvedorIa.Criar(Guid.NewGuid(), "DeepSeek", url, AutenticacaoProvedorIa.XApiKey, orcamento, Agora);

    [Fact]
    public void NasceDesligadoESemChave()
    {
        var p = Criar();

        p.Ligado.Should().BeFalse();
        p.CredencialId.Should().BeNull();
        p.ProviderKey.Should().Be($"ia:{p.Id}");
        p.CriadoEm.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void OrcamentoNuloEhSemCota()
    {
        Criar(orcamento: null).OrcamentoMensalUsd.Should().BeNull();
    }

    [Fact]
    public void RecusaOrcamentoNegativo()
    {
        var act = () => Criar(orcamento: -1m);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Theory]
    [InlineData("http://api.deepseek.com")]
    [InlineData("api.deepseek.com")]
    [InlineData("")]
    public void ExigeUrlHttpsAbsoluta(string url)
    {
        var act = () => Criar(url: url);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void RecusaEmpresaVazia()
    {
        var act = () => ProvedorIa.Criar(Guid.Empty, "DeepSeek", "https://api.deepseek.com", AutenticacaoProvedorIa.XApiKey, null, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }

    [Fact]
    public void SoLigaComChave()
    {
        var p = Criar();

        var semChave = () => p.Ligar(Agora);
        semChave.Should().Throw<RegraDeDominioVioladaException>();

        p.VincularCredencial(Guid.NewGuid(), Agora);
        p.Ligar(Agora);
        p.Ligado.Should().BeTrue();

        p.Desligar(Agora);
        p.Ligado.Should().BeFalse();
    }

    [Fact]
    public void TrocaOrcamento()
    {
        var p = Criar();
        p.DefinirOrcamento(50m, Agora);
        p.OrcamentoMensalUsd.Should().Be(50m);

        var act = () => p.DefinirOrcamento(-0.01m, Agora);
        act.Should().Throw<RegraDeDominioVioladaException>();
    }
}
