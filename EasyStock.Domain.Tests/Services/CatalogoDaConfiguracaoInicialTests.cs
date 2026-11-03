using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

/// <summary>#2070: o catálogo fechado dos passos é o contrato do assistente com o console.</summary>
public class CatalogoDaConfiguracaoInicialTests
{
    [Fact]
    public void CatalogoTemOsPassosDaEspecificacaoNestaOrdemEParte()
    {
        ConfiguracaoInicial.Passos.Select(p => (p.Id, p.Parte)).Should().Equal(
            ("boas-vindas", ParteDaConfiguracaoInicial.Essencial),
            ("horarios", ParteDaConfiguracaoInicial.Essencial),
            ("site-e-impressos", ParteDaConfiguracaoInicial.Essencial),
            ("cardapio", ParteDaConfiguracaoInicial.Essencial),
            ("janelas", ParteDaConfiguracaoInicial.Essencial),
            ("frete", ParteDaConfiguracaoInicial.Essencial),
            ("equipe", ParteDaConfiguracaoInicial.Essencial),
            ("dados-da-loja", ParteDaConfiguracaoInicial.Ajustes),
            ("atendimento", ParteDaConfiguracaoInicial.Ajustes),
            ("respostas", ParteDaConfiguracaoInicial.Ajustes),
            ("agente", ParteDaConfiguracaoInicial.Ajustes),
            ("producao", ParteDaConfiguracaoInicial.Ajustes),
            ("estoque", ParteDaConfiguracaoInicial.Ajustes),
            ("entregadores", ParteDaConfiguracaoInicial.Ajustes),
            ("impressora", ParteDaConfiguracaoInicial.Ajustes),
            ("avisos", ParteDaConfiguracaoInicial.Ajustes),
            ("pronto", ParteDaConfiguracaoInicial.Revisao));
    }

    [Fact]
    public void OrdemEhSequencialAPartirDeUm() =>
        ConfiguracaoInicial.Passos.Select(p => p.Ordem).Should().Equal(Enumerable.Range(1, 17));

    [Fact]
    public void IdsSaoKebabCaseECabemNaColuna() =>
        ConfiguracaoInicial.Passos.Should().AllSatisfy(p =>
        {
            p.Id.Should().MatchRegex("^[a-z]+(-[a-z]+)*$");
            p.Id.Length.Should().BeLessThanOrEqualTo(ConfiguracaoInicial.TamanhoMaximoDoId);
        });

    [Theory]
    [InlineData("cardapio", 4)]
    [InlineData("pronto", 17)]
    public void EncontrarDevolveOPassoDoCatalogo(string id, int ordem) =>
        ConfiguracaoInicial.Encontrar(id)!.Ordem.Should().Be(ordem);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Cardapio")]
    [InlineData("cardapio ")]
    [InlineData("caixa")]
    public void EncontrarDevolveNuloForaDoCatalogo(string? id) =>
        ConfiguracaoInicial.Encontrar(id).Should().BeNull();
}
