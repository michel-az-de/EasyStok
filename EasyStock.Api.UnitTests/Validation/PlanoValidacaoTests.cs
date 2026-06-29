using EasyStock.Api.Validation;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Validation;

/// <summary>
/// BUG-002/003 (QA 2026-06-04): regras de validacao de plano na fronteira da API.
/// Fecha a inconsistencia em que Cupom validava e Plano aceitava negativos: o QA criou
/// um plano com preco -50 e limites -5/-10/-100 que persistiram. Limite -1 = ilimitado
/// (sentinela), valores menores que -1 sao invalidos.
/// </summary>
public class PlanoValidacaoTests
{
    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public void ValidarNome_rejeita_curto(string nome)
        => PlanoValidacao.ValidarNome(nome).Should().NotBeNull();

    [Fact]
    public void ValidarNome_rejeita_acima_de_80_chars()
        => PlanoValidacao.ValidarNome(new string('A', 81)).Should().NotBeNull();

    [Theory]
    [InlineData("Pro")]
    [InlineData("Plano Profissional")]
    public void ValidarNome_aceita_valido(string nome)
        => PlanoValidacao.ValidarNome(nome).Should().BeNull();

    [Theory]
    [InlineData(-2)]
    [InlineData(-5)]
    [InlineData(-100)]
    public void ValidarLimite_rejeita_abaixo_de_menos_um(int valor)
        => PlanoValidacao.ValidarLimite(valor, "Limite de lojas").Should().NotBeNull();

    [Theory]
    [InlineData(-1)]   // sentinela ilimitado
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(99999)]
    public void ValidarLimite_aceita_menos_um_e_nao_negativos(int valor)
        => PlanoValidacao.ValidarLimite(valor, "Limite de lojas").Should().BeNull();

    [Fact]
    public void ValidarPreco_rejeita_negativo()
    {
        PlanoValidacao.ValidarPreco(-0.01m).Should().NotBeNull();
        PlanoValidacao.ValidarPreco(-50m).Should().NotBeNull();
    }

    [Fact]
    public void ValidarPreco_aceita_nao_negativo()
    {
        PlanoValidacao.ValidarPreco(0m).Should().BeNull();
        PlanoValidacao.ValidarPreco(49.90m).Should().BeNull();
    }

    // ADM-07 (#639): guardrail de plano gratuito.
    [Fact]
    public void ValidarPlanoGratuito_rejeita_ilimitado()
        => PlanoValidacao.ValidarPlanoGratuito(0m, PlanoValidacao.SemLimite, 5, 100, 10).Should().NotBeNull();

    [Fact]
    public void ValidarPlanoGratuito_rejeita_limites_altos()
        => PlanoValidacao.ValidarPlanoGratuito(0m, 999, 999, 99999, 100).Should().NotBeNull();

    [Fact]
    public void ValidarPlanoGratuito_aceita_gratuito_modesto()
        => PlanoValidacao.ValidarPlanoGratuito(0m, 1, 5, 500, 10).Should().BeNull();

    [Fact]
    public void ValidarPlanoGratuito_ignora_plano_pago()
        => PlanoValidacao.ValidarPlanoGratuito(149.90m, PlanoValidacao.SemLimite, -1, -1, -1).Should().BeNull();

    // #743: politica de nome (nao conter nome de cliente).
    [Fact]
    public void ValidarNomeNaoColideComTenant_rejeita_nome_contendo_tenant()
        => PlanoValidacao.ValidarNomeNaoColideComTenant("Demo Comercial Casa da Baba", new[] { "Casa da Baba" })
            .Should().NotBeNull();

    [Fact]
    public void ValidarNomeNaoColideComTenant_ignora_tenant_curto()
        => PlanoValidacao.ValidarNomeNaoColideComTenant("Profissional", new[] { "Pro" })
            .Should().BeNull();

    [Fact]
    public void ValidarNomeNaoColideComTenant_aceita_nome_generico()
        => PlanoValidacao.ValidarNomeNaoColideComTenant("Profissional", new[] { "Casa da Baba", "Padaria do Joao" })
            .Should().BeNull();
}
