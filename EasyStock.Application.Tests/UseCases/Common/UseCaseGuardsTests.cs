namespace EasyStock.Application.Tests.UseCases.Common;

public class UseCaseGuardsTests
{
    // BUG-05: nomes com tags HTML eram aceitos no input (XSS armazenado potencial em
    // contextos sem escape, ex.: PDF/etiqueta). EnsureSemTagsHtml bloqueia padroes de tag
    // (<tag), liberando < e > isolados para nao rejeitar nomes legitimos ("Tamanho > M").
    [Theory]
    [InlineData("<script>alert(1)</script>Bolo")]
    [InlineData("Camiseta <b>nova</b>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    public void EnsureSemTagsHtml_rejeita_tags_html(string nome)
    {
        Action act = () => UseCaseGuards.EnsureSemTagsHtml(nome, "Nome do produto");

        act.Should().Throw<UseCaseValidationException>()
            .WithMessage("*não pode conter tags HTML*");
    }

    [Theory]
    [InlineData("Bolo de cenoura")]
    [InlineData("Massa fresca 500g")]
    [InlineData("Preço > custo")]
    [InlineData("Tamanho > M")]
    [InlineData("Loja <3")]
    [InlineData("2 < 3 unidades")]
    [InlineData(null)]
    [InlineData("")]
    public void EnsureSemTagsHtml_aceita_texto_sem_tags(string? nome)
    {
        Action act = () => UseCaseGuards.EnsureSemTagsHtml(nome, "Nome do produto");

        act.Should().NotThrow();
    }

    // PROD-002 (#612): variante sanitizadora usada onde nao da pra rejeitar (auto-link mobile).
    [Theory]
    [InlineData("<script>alert(1)</script>Bolo", "alert(1)Bolo")]
    [InlineData("Camiseta <b>nova</b>", "Camiseta nova")]
    [InlineData("<img src=x onerror=alert(1)>", "")]
    public void RemoverTagsHtml_remove_tags(string entrada, string esperado)
    {
        UseCaseGuards.RemoverTagsHtml(entrada).Should().Be(esperado);
    }

    [Theory]
    [InlineData("Bolo de cenoura")]
    [InlineData("Tamanho > M")]
    [InlineData("Loja <3")]
    [InlineData("2 < 3 unidades")]
    public void RemoverTagsHtml_preserva_texto_sem_tags(string entrada)
    {
        UseCaseGuards.RemoverTagsHtml(entrada).Should().Be(entrada);
    }

    [Fact]
    public void RemoverTagsHtml_aceita_null_e_vazio()
    {
        UseCaseGuards.RemoverTagsHtml(null).Should().BeNull();
        UseCaseGuards.RemoverTagsHtml("").Should().Be("");
    }
}
