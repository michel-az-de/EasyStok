using System;
using EasyStock.Domain.Enums.Fiscal;
using EasyStock.Domain.ValueObjects.Fiscal;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects.Fiscal;

public class ChaveAcessoNFeTests
{
    [Fact]
    public void Construir_gera_chave_de_44_digitos_com_dv_valido()
    {
        var chave = ChaveAcessoNFe.Construir(
            ufCodigoIbge: "35",
            dhEmi: new DateTime(2026, 5, 8, 14, 0, 0, DateTimeKind.Utc),
            cnpj: "12345678000190",
            modelo: ModeloDocumentoFiscal.NFCe,
            serie: 1,
            numero: 1234,
            tpEmis: TipoEmissao.Normal,
            codigoNumericoOitoDigitos: "12345678");

        chave.Valor.Length.Should().Be(44);
        chave.Modelo.Should().Be("65");
        chave.Serie.Should().Be("001");
        chave.Numero.Should().Be("000001234");
        chave.TipoEmissaoStr.Should().Be("1");
        chave.CodigoNumerico.Should().Be("12345678");
    }

    [Fact]
    public void Construir_e_parse_devolvem_chaves_iguais()
    {
        var construida = ChaveAcessoNFe.Construir(
            "35", new DateTime(2026, 5, 8), "12345678000190",
            ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001");

        var parsed = ChaveAcessoNFe.Parse(construida.Valor);

        parsed.Should().Be(construida);
    }

    [Fact]
    public void Parse_chave_com_43_digitos_lanca_exception()
    {
        var act = () => ChaveAcessoNFe.Parse(new string('1', 43));
        act.Should().Throw<ArgumentException>().WithMessage("*44 dígitos*");
    }

    [Fact]
    public void Parse_chave_com_dv_invalido_lanca_exception()
    {
        var construida = ChaveAcessoNFe.Construir(
            "35", new DateTime(2026, 5, 8), "12345678000190",
            ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001");

        var dvCorreto = construida.Valor[43];
        var dvErrado = dvCorreto == '0' ? '1' : '0';
        var corrompida = construida.Valor[..43] + dvErrado;

        var act = () => ChaveAcessoNFe.Parse(corrompida);
        act.Should().Throw<ArgumentException>().WithMessage("*verificador*");
    }

    [Fact]
    public void Parse_aceita_chave_com_separadores_e_remove_eles()
    {
        var construida = ChaveAcessoNFe.Construir(
            "35", new DateTime(2026, 5, 8), "12345678000190",
            ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001");

        var comEspacos = construida.Formatada;

        var parsed = ChaveAcessoNFe.Parse(comEspacos);

        parsed.Valor.Should().Be(construida.Valor);
    }

    [Fact]
    public void Construir_com_cnpj_invalido_lanca_exception()
    {
        var act = () => ChaveAcessoNFe.Construir(
            "35", new DateTime(2026, 5, 8), "123",
            ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001");

        act.Should().Throw<ArgumentException>().WithMessage("*CNPJ*14 dígitos*");
    }

    [Fact]
    public void Construir_com_serie_invalida_lanca_exception()
    {
        var act = () => ChaveAcessoNFe.Construir(
            "35", new DateTime(2026, 5, 8), "12345678000190",
            ModeloDocumentoFiscal.NFCe, 0, 1, TipoEmissao.Normal, "00000001");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construir_com_numero_zero_lanca_exception()
    {
        var act = () => ChaveAcessoNFe.Construir(
            "35", new DateTime(2026, 5, 8), "12345678000190",
            ModeloDocumentoFiscal.NFCe, 1, 0, TipoEmissao.Normal, "00000001");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Formatada_separa_em_grupos_de_4()
    {
        var chave = ChaveAcessoNFe.Construir(
            "35", new DateTime(2026, 5, 8), "12345678000190",
            ModeloDocumentoFiscal.NFCe, 1, 1, TipoEmissao.Normal, "00000001");

        var formatada = chave.Formatada;

        formatada.Split(' ').Should().HaveCount(11);
        formatada.Split(' ').Should().AllSatisfy(g => g.Length.Should().Be(4));
        formatada.Replace(" ", "").Should().Be(chave.Valor);
    }
}
