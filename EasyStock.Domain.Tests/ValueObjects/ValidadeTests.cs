using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class ValidadeTests
{
    [Fact]
    public void Deve_criar_validade_com_data_normalizada()
    {
        var data = new DateTime(2024, 12, 31, 23, 59, 59);

        var validade = Validade.From(data);

        validade.DataValidade.Should().Be(new DateTime(2024, 12, 31));
    }

    [Fact]
    public void Deve_estar_vencido_quando_data_referencia_for_posterior()
    {
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2025, 1, 1);

        var vencido = validade.EstaVencido(referencia);

        vencido.Should().BeTrue();
    }

    [Fact]
    public void Nao_deve_estar_vencido_quando_data_referencia_for_anterior()
    {
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2024, 12, 30);

        var vencido = validade.EstaVencido(referencia);

        vencido.Should().BeFalse();
    }

    [Fact]
    public void Deve_calcular_dias_ate_vencimento()
    {
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2024, 12, 29);

        var dias = validade.DiasAteVencimento(referencia);

        dias.Should().Be(2);
    }

    [Fact]
    public void Deve_estar_pronto_para_vencer_quando_dentro_da_janela()
    {
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2024, 12, 29);
        var dias = 3;

        var pronto = validade.EstaProntoParaVencerEm(dias, referencia);

        pronto.Should().BeTrue();
    }

    [Fact]
    public void Nao_deve_estar_pronto_para_vencer_quando_fora_da_janela()
    {
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var referencia = new DateTime(2024, 12, 25);
        var dias = 3;

        var pronto = validade.EstaProntoParaVencerEm(dias, referencia);

        pronto.Should().BeFalse();
    }

    [Fact]
    public void Nao_deve_permitir_dias_negativo_em_esta_pronto_para_vencer()
    {
        var validade = Validade.From(new DateTime(2024, 12, 31));
        var dias = -1;

        Action act = () => validade.EstaProntoParaVencerEm(dias);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Dias não pode ser negativo.*");
    }

    [Fact]
    public void Deve_retornar_string_formatada()
    {
        var validade = Validade.From(new DateTime(2024, 12, 31));

        var str = validade.ToString();

        str.Should().Be("2024-12-31");
    }

    [Fact]
    public void Deve_ser_igual_por_data()
    {
        var v1 = Validade.From(new DateTime(2024, 12, 31));
        var v2 = Validade.From(new DateTime(2024, 12, 31));

        v1.Should().Be(v2);
    }
}