using EasyStock.Web.Services;
using FluentAssertions;

namespace EasyStock.Web.UnitTests.Services;

/// <summary>
/// Trava o "tradutor" de erros para o usuário — a peça que garante que um erro
/// NUNCA chega como "ocorreu um erro genérico" sem orientação. Regras:
/// (1) mensagem específica e legível da API passa intacta;
/// (2) dumps técnicos / placeholders são substituídos;
/// (3) código conhecido vira texto amigável;
/// (4) todo status HTTP tem um fallback orientado à ação.
/// </summary>
public class UserFacingErrorsTests
{
    [Fact]
    public void Sanitize_PreservaMensagemEspecificaDaApi()
    {
        const string msg = "Categoria de receita não pode ser usada em conta a pagar.";
        UserFacingErrors.Sanitize("BUSINESS_RULE_VIOLATION", msg, 409).Should().Be(msg);
    }

    [Fact]
    public void Sanitize_RemoveSufixoParameter_PreservandoMensagem()
    {
        // BUG-08: ArgumentException.Message vem com " (Parameter 'x')"; o usuario nao deve ve-lo,
        // mas a mensagem de negocio precisa ser preservada.
        UserFacingErrors.Sanitize(null, "Valor monetário não pode ser negativo. (Parameter 'valor')", 400)
            .Should().Be("Valor monetário não pode ser negativo.");
    }

    [Theory]
    [InlineData("System.NullReferenceException: Object reference not set")]
    [InlineData("at EasyStock.Api.Foo.Bar(Baz x)")]
    [InlineData("Bad Request")]
    [InlineData("Internal Server Error")]
    [InlineData("An error occurred.")]
    [InlineData("{\"trace\":\"abc\"}")]
    public void Sanitize_SubstituiMensagensTecnicasOuPlaceholders(string tecnica)
    {
        var result = UserFacingErrors.Sanitize(null, tecnica, 400);

        result.Should().NotBe(tecnica);
        result.Should().Be(UserFacingErrors.FallbackForStatus(400));
    }

    [Fact]
    public void Sanitize_UsaTraducaoDoCodigo_QuandoSemMensagem()
    {
        UserFacingErrors.Sanitize("SKU_DUPLICADO", null, 409)
            .Should().Be("Já existe um produto com esse SKU.");
    }

    [Fact]
    public void Sanitize_UsaTraducaoDoCodigo_QuandoMensagemEhTecnica()
    {
        UserFacingErrors.Sanitize("EMAIL_DUPLICADO", "System.Exception: dup key", 409)
            .Should().Be("Este e-mail já está em uso.");
    }

    [Fact]
    public void Sanitize_SemMensagemNemCodigoConhecido_CaiNoFallbackDeStatus()
    {
        UserFacingErrors.Sanitize("ALGUM_CODIGO_NOVO", null, 404)
            .Should().Be(UserFacingErrors.FallbackForStatus(404));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public void FallbackForStatus_SempreOrientadoEnuncaTecnico(int status)
    {
        var msg = UserFacingErrors.FallbackForStatus(status);

        msg.Should().NotBeNullOrWhiteSpace();
        UserFacingErrors.IsTechnical(msg).Should().BeFalse();
    }

    [Fact]
    public void FallbackForStatus_StatusDesconhecido_IncluiOCodigoParaSuporte()
    {
        UserFacingErrors.FallbackForStatus(418).Should().Contain("418");
    }

    [Fact]
    public void IsTechnical_DetectaDumpsEPlaceholders_MasNaoMensagensDeNegocio()
    {
        UserFacingErrors.IsTechnical("Internal Server Error").Should().BeTrue();
        UserFacingErrors.IsTechnical("NullReferenceException").Should().BeTrue();
        UserFacingErrors.IsTechnical("at Foo.Bar.Baz").Should().BeTrue();
        UserFacingErrors.IsTechnical("Estoque insuficiente para esta operação.").Should().BeFalse();
        UserFacingErrors.IsTechnical(null).Should().BeFalse();
    }
}
