using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class CodigoLoteTests
{
    [Fact]
    public void Deve_criar_codigo_lote_quando_valido()
    {
        var value = "LOT-001/2024";

        var lote = CodigoLote.From(value);

        lote.Value.Should().Be("LOT-001/2024");
    }

    [Fact]
    public void Deve_normalizar_para_maiusculo()
    {
        var value = "lot-001/2024";

        var lote = CodigoLote.From(value);

        lote.Value.Should().Be("LOT-001/2024");
    }

    [Fact]
    public void Deve_trim_whitespace()
    {
        var value = "  LOT-001  ";

        var lote = CodigoLote.From(value);

        lote.Value.Should().Be("LOT-001");
    }

    [Fact]
    public void Nao_deve_permitir_codigo_vazio()
    {
        var value = "";

        Action act = () => CodigoLote.From(value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Código de lote é obrigatório.*");
    }

    [Fact]
    public void Nao_deve_permitir_codigo_muito_longo()
    {
        var value = new string('A', 101);

        Action act = () => CodigoLote.From(value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Codigo de lote muito longo.*");
    }

    [Fact]
    public void Nao_deve_permitir_caracteres_invalidos()
    {
        var value = "LOT@001";

        Action act = () => CodigoLote.From(value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Codigo de lote contem caracteres invalidos.*");
    }

    [Fact]
    public void Deve_permitir_letras_digitos_traco_underline_barra()
    {
        var value = "L1_T-1/24";

        var lote = CodigoLote.From(value);

        lote.Value.Should().Be("L1_T-1/24");
    }

    [Fact]
    public void Deve_ser_igual_por_valor()
    {
        var l1 = CodigoLote.From("LOT-001");
        var l2 = CodigoLote.From("LOT-001");

        l1.Should().Be(l2);
    }

    [Fact]
    public void Deve_ser_diferente_por_valor()
    {
        var l1 = CodigoLote.From("LOT-001");
        var l2 = CodigoLote.From("LOT-002");

        l1.Should().NotBe(l2);
    }

    [Fact]
    public void Deve_retornar_string_do_valor()
    {
        var lote = CodigoLote.From("LOT-001");

        var str = lote.ToString();

        str.Should().Be("LOT-001");
    }
}