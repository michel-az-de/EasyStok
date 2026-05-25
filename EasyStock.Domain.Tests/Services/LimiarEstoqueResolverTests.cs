using EasyStock.Domain.Defaults;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Services;
using FluentAssertions;

namespace EasyStock.Domain.Tests.Services;

public class LimiarEstoqueResolverTests
{
    [Fact]
    public void Sem_overrides_deve_retornar_defaults_globais()
    {
        var produto = new Produto { Nome = "X" };

        var limiares = LimiarEstoqueResolver.Resolver(produto, null, null);

        limiares.QuantidadeMinima.Should().Be(OperacionalDefaults.QuantidadeMinima);
        limiares.QuantidadeCritica.Should().Be(OperacionalDefaults.QuantidadeCritica);
    }

    [Fact]
    public void ConfiguracaoLoja_sobrescreve_default_quando_produto_e_categoria_nulos()
    {
        var produto = new Produto { Nome = "X" };
        var configuracao = ConfiguracaoLoja.CriarPadrao(Guid.NewGuid());
        configuracao.QuantidadeMinimaPadrao = 20;
        configuracao.QuantidadeCriticaPadrao = 5;

        var limiares = LimiarEstoqueResolver.Resolver(produto, null, configuracao);

        limiares.QuantidadeMinima.Should().Be(20);
        limiares.QuantidadeCritica.Should().Be(5);
    }

    [Fact]
    public void Categoria_sobrescreve_loja_quando_produto_nao_define()
    {
        var produto = new Produto { Nome = "X" };
        var categoria = new Categoria { Nome = "Especiarias", QuantidadeMinima = 30, QuantidadeCritica = 10 };
        var configuracao = ConfiguracaoLoja.CriarPadrao(Guid.NewGuid());
        configuracao.QuantidadeMinimaPadrao = 20;

        var limiares = LimiarEstoqueResolver.Resolver(produto, categoria, configuracao);

        limiares.QuantidadeMinima.Should().Be(30);
        limiares.QuantidadeCritica.Should().Be(10);
    }

    [Fact]
    public void Produto_vence_categoria_e_loja()
    {
        var produto = new Produto { Nome = "Sal", QuantidadeMinima = 50, QuantidadeCritica = 15 };
        var categoria = new Categoria { Nome = "Tempero", QuantidadeMinima = 30, QuantidadeCritica = 10 };
        var configuracao = ConfiguracaoLoja.CriarPadrao(Guid.NewGuid());
        configuracao.QuantidadeMinimaPadrao = 20;

        var limiares = LimiarEstoqueResolver.Resolver(produto, categoria, configuracao);

        limiares.QuantidadeMinima.Should().Be(50);
        limiares.QuantidadeCritica.Should().Be(15);
    }

    [Fact]
    public void Hierarquia_resolve_minima_e_critica_em_niveis_diferentes()
    {
        // Produto define só a mínima; crítica vem da categoria.
        var produto = new Produto { Nome = "Y", QuantidadeMinima = 100, QuantidadeCritica = null };
        var categoria = new Categoria { Nome = "C", QuantidadeMinima = null, QuantidadeCritica = 20 };

        var limiares = LimiarEstoqueResolver.Resolver(produto, categoria, null);

        limiares.QuantidadeMinima.Should().Be(100);
        limiares.QuantidadeCritica.Should().Be(20);
    }

    [Fact]
    public void Critica_nunca_pode_ficar_maior_ou_igual_a_minima()
    {
        // Configuração inválida feita pelo usuário: critica > minima → resolver normaliza.
        var produto = new Produto { Nome = "Z", QuantidadeMinima = 5, QuantidadeCritica = 8 };

        var limiares = LimiarEstoqueResolver.Resolver(produto, null, null);

        limiares.QuantidadeMinima.Should().Be(5);
        limiares.QuantidadeCritica.Should().BeLessThan(5);
        limiares.QuantidadeCritica.Should().Be(4);
    }
}
