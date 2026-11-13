using EasyStock.Infra.Async.Email;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>#2074: a parte texto que acompanha todo e-mail HTML.</summary>
public class TextoDoHtmlTests
{
    [Fact]
    public void Tira_estilo_e_tags_e_guarda_paragrafos_e_entidades()
    {
        const string html = "<html><head><meta charset=\"utf-8\"><style>p{color:red}</style></head><body>"
            + "<h2>Seu convite chegou</h2><p>Olá, <strong>Maria</strong> &amp; equipe!</p>"
            + "<table><tr><td>Valor</td><td><strong>R$ 10,00</strong></td></tr></table></body></html>";

        TextoDoHtml.Converter(html).Should().Be("Seu convite chegou\n\nOlá, Maria & equipe!\n\nValor R$ 10,00");
    }

    [Fact]
    public void Link_mostra_o_texto_e_o_endereco()
    {
        TextoDoHtml.Converter("<p>Clique <a href=\"https://app.easystok.online/#/convite?t=abc\" style=\"x\">Aceitar o convite</a></p>")
            .Should().Be("Clique Aceitar o convite: https://app.easystok.online/#/convite?t=abc");
    }

    [Fact]
    public void Link_cujo_texto_e_o_proprio_endereco_nao_repete()
    {
        TextoDoHtml.Converter("<a href=\"https://x.test\">https://x.test</a>").Should().Be("https://x.test");
    }
}
