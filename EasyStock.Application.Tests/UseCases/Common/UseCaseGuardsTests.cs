namespace EasyStock.Application.Tests.UseCases.Common;

public class UseCaseGuardsTests
{
    // BUG-05 (#486): nomes de produto com tags HTML eram aceitos no input (XSS armazenado
    // potencial em contextos sem escape, ex.: PDF/etiqueta). EnsureSemTagsHtml bloqueia
    // os caracteres < e > na entrada.
    [Theory]
    [InlineData("<script>alert(1)</script>Bolo")]
    [InlineData("Camiseta <b>nova</b>")]
    [InlineData("Preço > custo")]
    public void EnsureSemTagsHtml_rejeita_caracteres_de_tag(string nome)
    {
        Action act = () => UseCaseGuards.EnsureSemTagsHtml(nome, "Nome do produto");

        act.Should().Throw<UseCaseValidationException>()
            .WithMessage("*não pode conter os caracteres*");
    }

    [Theory]
    [InlineData("Bolo de cenoura")]
    [InlineData("Massa fresca 500g")]
    [InlineData(null)]
    [InlineData("")]
    public void EnsureSemTagsHtml_aceita_texto_sem_tags(string? nome)
    {
        Action act = () => UseCaseGuards.EnsureSemTagsHtml(nome, "Nome do produto");

        act.Should().NotThrow();
    }
}
