using EasyStock.Application.UseCases.Storefront.Avaliacao;
using FluentAssertions;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Storefront.Avaliacao;

/// <summary>
/// Testes do <see cref="ComentarioSanitizer"/> (TASK-EZ-AVAL-001).
/// </summary>
public sealed class ComentarioSanitizerTests
{
    private readonly ComentarioSanitizer _sut = new();

    [Fact]
    public void Sanitizar_StringLimpa_RetornaIntacta()
    {
        var resultado = _sut.Sanitizar("Adorei o serviço!");

        resultado.Should().Be("Adorei o serviço!");
    }

    [Fact]
    public void Sanitizar_ComTagsHtml_RemoveTags()
    {
        var resultado = _sut.Sanitizar("Ótimo! <script>alert('xss')</script>");

        resultado.Should().NotContain("<script>");
        resultado.Should().NotContain("</script>");
        resultado.Should().Contain("Ótimo!");
    }

    [Fact]
    public void Sanitizar_TagsHtmlMistas_RemoveTodas()
    {
        var resultado = _sut.Sanitizar("<b>Excelente</b> <i>atendimento</i>");

        resultado.Should().NotContain("<b>");
        resultado.Should().NotContain("<i>");
        resultado.Should().Contain("Excelente");
        resultado.Should().Contain("atendimento");
    }

    [Fact]
    public void Sanitizar_Acima500Chars_TruncaEm500()
    {
        var longo = new string('a', 501);

        var resultado = _sut.Sanitizar(longo);

        resultado.Should().HaveLength(500);
    }

    [Fact]
    public void Sanitizar_Exatamente500Chars_RetornaIntacto()
    {
        var exato = new string('a', 500);

        var resultado = _sut.Sanitizar(exato);

        resultado.Should().HaveLength(500);
    }

    [Fact]
    public void Sanitizar_ComProfanity_SubstitiuPorAsteriscos()
    {
        // Usa uma palavra da lista de profanity do sanitizer
        var resultado = _sut.Sanitizar("Que merda esse serviço");

        resultado.Should().Contain("***");
        resultado.Should().NotContainAny("merda");
    }

    [Fact]
    public void Sanitizar_StringVazia_RetornaVazia()
    {
        var resultado = _sut.Sanitizar("");

        resultado.Should().BeEmpty();
    }

    [Fact]
    public void Sanitizar_StringNula_RetornaVazia()
    {
        var resultado = _sut.Sanitizar(null!);

        resultado.Should().BeEmpty();
    }

    [Fact]
    public void Sanitizar_ApenasEspacos_RetornaVazia()
    {
        var resultado = _sut.Sanitizar("   ");

        resultado.Should().BeEmpty();
    }
}
