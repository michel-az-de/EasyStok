using System.Text.Json;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class TelefoneTests
{
    [Theory]
    [InlineData("11999998888", "11999998888")]
    [InlineData("(11) 99999-8888", "11999998888")]
    [InlineData("11 99999-8888", "11999998888")]
    [InlineData("+5511999998888", "+5511999998888")]
    [InlineData("+1 (415) 555-1234", "+14155551234")]
    [InlineData("  (21) 2222-3333  ", "2122223333")]
    public void From_normaliza_removendo_mascara_e_espacos(string input, string expected)
    {
        Telefone.From(input).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void From_rejeita_quando_vazio_ou_nulo(string? input)
    {
        var act = () => Telefone.From(input!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("123456")]            // 6 digitos < 7
    [InlineData("1234567890123456")]  // 16 digitos > 15
    [InlineData("abcdefghij")]        // letras
    [InlineData("11-99999-8888x")]    // caractere invalido residual
    public void From_rejeita_quando_formato_invalido(string input)
    {
        var act = () => Telefone.From(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFrom_retorna_null_para_entrada_invalida()
    {
        Telefone.TryFrom(null).Should().BeNull();
        Telefone.TryFrom("").Should().BeNull();
        Telefone.TryFrom("xyz").Should().BeNull();
    }

    [Fact]
    public void TryFrom_retorna_telefone_para_entrada_valida()
    {
        Telefone.TryFrom("11999998888")!.Value.Should().Be("11999998888");
    }

    [Fact]
    public void ImplicitOperator_para_string_retorna_value_normalizado()
    {
        var telefone = Telefone.From("(11) 99999-8888");
        string str = telefone;
        str.Should().Be("11999998888");
    }

    [Fact]
    public void ToString_retorna_value_normalizado()
    {
        Telefone.From("+55 11 99999-8888").ToString().Should().Be("+5511999998888");
    }

    [Fact]
    public void Equality_e_estrutural_apos_normalizacao()
    {
        var a = Telefone.From("(11) 99999-8888");
        var b = Telefone.From("11999998888");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void JsonRoundtrip_preserva_valor()
    {
        var original = Telefone.From("+5511999998888");
        var json = JsonSerializer.Serialize(original);

        var deserialized = JsonSerializer.Deserialize<Telefone>(json);
        deserialized.Should().NotBeNull();
        deserialized!.Value.Should().Be("+5511999998888");
    }

    [Fact]
    public void JsonSerialize_emite_string_apenas_com_value()
    {
        var telefone = Telefone.From("11999998888");
        var json = JsonSerializer.Serialize(telefone);
        json.Should().Be("\"11999998888\"");
    }

    [Fact]
    public void JsonDeserialize_de_null_retorna_null()
    {
        var deserialized = JsonSerializer.Deserialize<Telefone>("null");
        deserialized.Should().BeNull();
    }
}
