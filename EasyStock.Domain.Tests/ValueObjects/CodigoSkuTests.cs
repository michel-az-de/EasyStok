using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class CodigoSkuTests
{
    [Fact]
    public void Deve_criar_codigo_sku_quando_valido()
    {
        var value = "ABC-123";

        var sku = CodigoSku.From(value);

        sku.Value.Should().Be("ABC-123");
    }

    [Fact]
    public void Deve_normalizar_para_maiusculo()
    {
        var value = "abc-123";

        var sku = CodigoSku.From(value);

        sku.Value.Should().Be("ABC-123");
    }

    [Fact]
    public void Deve_trim_whitespace()
    {
        var value = "  ABC-123  ";

        var sku = CodigoSku.From(value);

        sku.Value.Should().Be("ABC-123");
    }

    [Fact]
    public void Nao_deve_permitir_sku_vazio()
    {
        var value = "";

        Action act = () => CodigoSku.From(value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SKU é obrigatório.*");
    }

    [Fact]
    public void Nao_deve_permitir_sku_whitespace()
    {
        var value = "   ";

        Action act = () => CodigoSku.From(value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SKU é obrigatório.*");
    }

    [Fact]
    public void Nao_deve_permitir_sku_muito_longo()
    {
        var value = new string('A', 101);

        Action act = () => CodigoSku.From(value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SKU muito longo.*");
    }

    [Fact]
    public void Nao_deve_permitir_caracteres_invalidos()
    {
        var value = "ABC@123";

        Action act = () => CodigoSku.From(value);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SKU contém caracteres inválidos.*");
    }

    [Fact]
    public void Deve_permitir_letras_digitos_traco_underline()
    {
        var value = "A1_B-2";

        var sku = CodigoSku.From(value);

        sku.Value.Should().Be("A1_B-2");
    }

    [Fact]
    public void Deve_ser_igual_por_valor()
    {
        var s1 = CodigoSku.From("ABC-123");
        var s2 = CodigoSku.From("ABC-123");

        s1.Should().Be(s2);
    }

    [Fact]
    public void Deve_ser_diferente_por_valor()
    {
        var s1 = CodigoSku.From("ABC-123");
        var s2 = CodigoSku.From("DEF-456");

        s1.Should().NotBe(s2);
    }

    [Fact]
    public void Deve_retornar_string_do_valor()
    {
        var sku = CodigoSku.From("ABC-123");

        var str = sku.ToString();

        str.Should().Be("ABC-123");
    }
}
