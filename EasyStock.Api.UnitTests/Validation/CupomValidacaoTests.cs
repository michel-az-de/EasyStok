using EasyStock.Api.Validation;
using EasyStock.Domain.Enums;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Validation;

/// <summary>
/// INV-001 (#463): regras de validacao de cupom na fronteira da API. O charset
/// rejeita aspas/&lt;&gt; (vetor DOM-XSS no Admin) e o teto de 100% impede o
/// percentual 99999 que o QA tentou criar.
/// </summary>
public class CupomValidacaoTests
{
    [Theory]
    [InlineData("AB")]          // curto demais
    [InlineData("PROMO 10")]    // espaco
    [InlineData("PROMO'10")]    // aspa (vetor Alpine)
    [InlineData("<SCRIPT>")]    // angle brackets
    [InlineData("PROMO@10")]    // caractere fora do conjunto
    public void ValidarCodigo_rejeita_invalido(string codigo)
        => CupomValidacao.ValidarCodigo(codigo).Should().NotBeNull();

    [Fact]
    public void ValidarCodigo_rejeita_acima_de_50_chars()
        => CupomValidacao.ValidarCodigo(new string('A', 51)).Should().NotBeNull();

    [Theory]
    [InlineData("PROMO10")]
    [InlineData("BLACK-FRIDAY_2026")]
    [InlineData("ABC")]
    public void ValidarCodigo_aceita_valido(string codigo)
        => CupomValidacao.ValidarCodigo(codigo).Should().BeNull();

    [Fact]
    public void ValidarValor_rejeita_percentual_acima_de_100()
        => CupomValidacao.ValidarValor(TipoDesconto.Percentual, 99999m).Should().NotBeNull();

    [Fact]
    public void ValidarValor_aceita_percentual_ate_100()
        => CupomValidacao.ValidarValor(TipoDesconto.Percentual, 100m).Should().BeNull();

    [Fact]
    public void ValidarValor_rejeita_zero_e_negativo()
    {
        CupomValidacao.ValidarValor(TipoDesconto.ValorFixo, 0m).Should().NotBeNull();
        CupomValidacao.ValidarValor(TipoDesconto.ValorFixo, -5m).Should().NotBeNull();
    }

    [Fact]
    public void ValidarValor_valorFixo_acima_de_100_e_permitido()
        => CupomValidacao.ValidarValor(TipoDesconto.ValorFixo, 150m).Should().BeNull();
}
