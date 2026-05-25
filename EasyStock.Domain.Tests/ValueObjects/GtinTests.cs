using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class GtinTests
{
    [Fact]
    public void Deve_aceitar_ean13_valido()
    {
        // 7891000100103 = Nestle Leite Moca (GTIN real).
        var gtin = Gtin.Parse("7891000100103");

        gtin.Valor.Should().Be("7891000100103");
        gtin.Tipo.Should().Be(TipoGtin.Ean13);
        gtin.EhInterno.Should().BeFalse();
    }

    [Fact]
    public void Deve_aceitar_ean8_valido()
    {
        // 96385074 = exemplo padrao GS1 EAN-8.
        var gtin = Gtin.Parse("96385074");

        gtin.Tipo.Should().Be(TipoGtin.Ean8);
        gtin.EhInterno.Should().BeFalse();
    }

    [Fact]
    public void Deve_aceitar_upc12_valido()
    {
        // 036000291452 = exemplo padrao UPC-A.
        var gtin = Gtin.Parse("036000291452");

        gtin.Tipo.Should().Be(TipoGtin.Upc12);
    }

    [Fact]
    public void Deve_aceitar_gtin14_valido()
    {
        // 10614141000415 = exemplo padrao GTIN-14 (caixa de varejo).
        var gtin = Gtin.Parse("10614141000415");

        gtin.Tipo.Should().Be(TipoGtin.Gtin14);
    }

    [Fact]
    public void Deve_marcar_ean13_iniciado_em_2_como_interno()
    {
        // Faixa GS1 reservada para uso interno (etiqueta de balanca etc).
        // 2000000000008: 2*1 = 2 -> (10-2) % 10 = 8 (digito verificador).
        var gtin = Gtin.Parse("2000000000008");

        gtin.Tipo.Should().Be(TipoGtin.Ean13);
        gtin.EhInterno.Should().BeTrue();
    }

    [Fact]
    public void Deve_aceitar_codigo_interno_com_prefixo_INT()
    {
        var gtin = Gtin.Parse("INT-ABCD1234-XYZ987");

        gtin.Tipo.Should().Be(TipoGtin.InternoCode128);
        gtin.EhInterno.Should().BeTrue();
        gtin.Valor.Should().Be("INT-ABCD1234-XYZ987");
    }

    [Fact]
    public void Deve_rejeitar_ean13_com_checksum_invalido()
    {
        // Ultimo digito mudado de 3 para 4 — invalida o checksum.
        Action act = () => Gtin.Parse("7891000100104");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*digito verificador nao confere*");
    }

    [Fact]
    public void Deve_rejeitar_codigo_com_letras_sem_prefixo_int()
    {
        Action act = () => Gtin.Parse("789ABC1234567");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*numerico*");
    }

    [Fact]
    public void Deve_rejeitar_tamanho_diferente_de_8_12_13_14()
    {
        Action act = () => Gtin.Parse("12345678901");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*8, 12, 13 ou 14 digitos*");
    }

    [Fact]
    public void Deve_rejeitar_vazio()
    {
        Action act = () => Gtin.Parse("");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*nao pode ser vazio*");
    }

    [Fact]
    public void Deve_rejeitar_whitespace()
    {
        Action act = () => Gtin.Parse("   ");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*nao pode ser vazio*");
    }

    [Fact]
    public void Deve_rejeitar_int_sem_conteudo()
    {
        Action act = () => Gtin.Parse("INT-");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*conteudo apos 'INT-'*");
    }

    [Fact]
    public void Deve_rejeitar_int_com_caracter_invalido()
    {
        Action act = () => Gtin.Parse("INT-ABC@123");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*apenas letras, digitos*");
    }

    [Fact]
    public void Deve_rejeitar_int_muito_longo()
    {
        // 4 prefixo + 47 sufixo = 51 caracteres (maior que 50).
        var longo = "INT-" + new string('A', 47);

        Action act = () => Gtin.Parse(longo);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*max 50 caracteres*");
    }

    [Fact]
    public void Deve_trim_whitespace_externo()
    {
        var gtin = Gtin.Parse("  7891000100103  ");

        gtin.Valor.Should().Be("7891000100103");
    }

    [Fact]
    public void TryParse_deve_retornar_false_em_invalido_sem_lancar()
    {
        var ok = Gtin.TryParse("invalido123", out var gtin);

        ok.Should().BeFalse();
        gtin.Should().BeNull();
    }

    [Fact]
    public void TryParse_deve_retornar_true_em_valido()
    {
        var ok = Gtin.TryParse("7891000100103", out var gtin);

        ok.Should().BeTrue();
        gtin.Should().NotBeNull();
        gtin!.Valor.Should().Be("7891000100103");
    }

    [Fact]
    public void TryParse_deve_retornar_false_em_null_ou_vazio()
    {
        Gtin.TryParse(null, out var g1).Should().BeFalse();
        Gtin.TryParse("", out var g2).Should().BeFalse();
        Gtin.TryParse("  ", out var g3).Should().BeFalse();

        g1.Should().BeNull();
        g2.Should().BeNull();
        g3.Should().BeNull();
    }

    [Fact]
    public void Implicit_string_conversion_deve_retornar_valor()
    {
        var gtin = Gtin.Parse("7891000100103");

        string s = gtin;

        s.Should().Be("7891000100103");
    }
}
