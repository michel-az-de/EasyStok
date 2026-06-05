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
}
