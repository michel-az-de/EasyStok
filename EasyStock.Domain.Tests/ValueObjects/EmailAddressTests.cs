using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Domain.Tests.ValueObjects;

public class EmailAddressTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("USER@EXAMPLE.COM")]
    [InlineData("user.name+tag@sub.domain.org")]
    public void From_aceita_emails_validos_e_normaliza_lowercase(string email)
    {
        var result = EmailAddress.From(email);
        result.Value.Should().Be(email.Trim().ToLowerInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nao-e-email")]
    [InlineData("@semdominio.com")]
    [InlineData("usuario@nodot")]
    public void From_rejeita_emails_invalidos(string? email)
    {
        var act = () => EmailAddress.From(email!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryFrom_retorna_null_para_email_invalido()
    {
        EmailAddress.TryFrom("invalido").Should().BeNull();
        EmailAddress.TryFrom(null).Should().BeNull();
    }

    [Fact]
    public void TryFrom_retorna_instancia_para_email_valido()
    {
        var result = EmailAddress.TryFrom("test@domain.com");
        result.Should().NotBeNull();
        result!.Value.Should().Be("test@domain.com");
    }

    [Fact]
    public void Implicit_operator_converte_para_string()
    {
        var email = EmailAddress.From("user@example.com");
        string str = email;
        str.Should().Be("user@example.com");
    }

    [Fact]
    public void Dois_emails_iguais_sao_iguais()
    {
        var a = EmailAddress.From("user@example.com");
        var b = EmailAddress.From("USER@EXAMPLE.COM");
        a.Should().Be(b);
    }
}
