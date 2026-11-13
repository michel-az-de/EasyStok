using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

/// <summary>#2018: o número curto do pedido é uma regra só, igual ao "ref" que o console mostra (8 letras, maiúsculas).</summary>
public class CodigoCurtoTests
{
    [Theory]
    [InlineData("a1b2c3d4-0000-0000-0000-000000000000", "A1B2C3D4")]
    [InlineData("0f9e8d7c-1111-2222-3333-444444444444", "0F9E8D7C")]
    public void SaoOsOitoPrimeirosHexadecimaisEmMaiusculas(string id, string esperado) =>
        CodigoCurto.De(Guid.Parse(id)).Should().Be(esperado);

    [Fact]
    public void ForaDoFormatoComHifenEDeMaiusculas()
    {
        var id = Guid.NewGuid();

        var codigo = CodigoCurto.De(id);

        codigo.Should().HaveLength(CodigoCurto.Tamanho).And.MatchRegex("^[0-9A-F]{8}$");
        id.ToString("N").ToUpperInvariant().Should().StartWith(codigo);
    }
}
