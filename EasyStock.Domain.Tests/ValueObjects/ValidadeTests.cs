using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class ValidadeTests
{
    [Fact]
    public void Deve_criar_validade_com_data_normalizada()
    {
        // Arrange
        var data = new DateTime(2024, 12, 31, 23, 59, 59);

        // Act
        var validade = Validade.From(data);

        // Assert
        validade.DataValidade.Should().Be(new DateTime(2024, 12, 31));
    }

    [Fact]
    public void Deve_estar_vencido_quando_data_referencia_for_posterior()
    {
        // Arrange
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2025, 1, 1);

        // Act
        var vencido = validade.EstaVencido(referencia);

        // Assert
        vencido.Should().BeTrue();
    }

    [Fact]
    public void Nao_deve_estar_vencido_quando_data_referencia_for_anterior()
    {
        // Arrange
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2024, 12, 30);

        // Act
        var vencido = validade.EstaVencido(referencia);

        // Assert
        vencido.Should().BeFalse();
    }

    [Fact]
    public void Deve_calcular_dias_ate_vencimento()
    {
        // Arrange
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2024, 12, 29);

        // Act
        var dias = validade.DiasAteVencimento(referencia);

        // Assert
        dias.Should().Be(2);
    }

    [Fact]
    public void Deve_estar_pronto_para_vencer_quando_dentro_da_janela()
    {
        // Arrange
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2024, 12, 29);
        var dias = 3;

        // Act
        var pronto = validade.EstaProntoParaVencerEm(dias, referencia);

        // Assert
        pronto.Should().BeTrue();
    }

    [Fact]
    public void Nao_deve_estar_pronto_para_vencer_quando_fora_da_janela()
    {
        // Arrange
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2024, 12, 25);
        var dias = 3;

        // Act
        var pronto = validade.EstaProntoParaVencerEm(dias, referencia);

        // Assert
        pronto.Should().BeFalse();
    }

    [Fact]
    public void Nao_deve_permitir_dias_negativo_em_esta_pronto_para_vencer()
    {
        // Arrange
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var dias = -1;

        // Act
        Action act = () => validade.EstaProntoParaVencerEm(dias);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Dias não pode ser negativo.*");
    }

    [Fact]
    public void Deve_retornar_string_formatada()
    {
        // Arrange
        var validade = Validade.From(new DateTime(2024, 12, 31));

        // Act
        var str = validade.ToString();

        // Assert
        str.Should().Be("2024-12-31");
    }

    [Fact]
    public void Deve_ser_igual_por_data()
    {
        // Arrange
        var v1 = Validade.From(new DateTime(2024, 12, 31));
        var v2 = Validade.From(new DateTime(2024, 12, 31));

        // Act & Assert
        v1.Should().Be(v2);
    }
}