using System.Text.Encodings.Web;
using EasyStock.Web.TagHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EasyStock.Web.UnitTests.TagHelpers;

/// <summary>
/// Caracteriza o port do StatCard do Admin (convergencia DS, #393). Como o app Web
/// nao roda local (Postgres+Docker ausentes), estes testes sao a verificacao da logica
/// que o usuario ve: cor do delta (verde/vermelho), tendencia do sparkline e estrutura
/// (link vs div). Testa via o Process() publico (output HTML real), nao helpers privados.
/// </summary>
public class EsStatCardTagHelperTests
{
    [Theory]
    [InlineData("+12.5%", null, "es-stat-delta-up")]
    [InlineData("-3.2%", null, "es-stat-delta-down")]
    [InlineData("0%", null, "es-stat-delta-flat")]
    [InlineData("5%", "up", "es-stat-delta-up")]    // trend explicito vence o sinal
    [InlineData("5%", "down", "es-stat-delta-down")]
    public void Delta_resolve_classe_de_tendencia(string delta, string? trend, string esperado)
    {
        var (content, _, _) = Render(new EsStatCardTagHelper { Label = "X", Value = "R$ 1", Delta = delta, DeltaTrend = trend });
        content.Should().Contain(esperado);
    }

    [Fact]
    public void Delta_negativo_com_minus_unicode_e_down()
    {
        // U+2212 (MINUS SIGN), nao o hifen ASCII — vem de alguns formatadores pt-BR.
        var (content, _, _) = Render(new EsStatCardTagHelper { Value = "1", Delta = "−5%" });
        content.Should().Contain("es-stat-delta-down");
    }

    [Fact]
    public void Sparkline_ascendente_e_is_up()
    {
        var (content, _, _) = Render(new EsStatCardTagHelper { Value = "1", Sparkline = "1,2,3,4,5" });
        content.Should().Contain("es-sparkline is-up");
        content.Should().Contain("es-sparkline-line");
    }

    [Fact]
    public void Sparkline_descendente_e_is_down()
    {
        var (content, _, _) = Render(new EsStatCardTagHelper { Value = "1", Sparkline = "5,4,3,2,1" });
        content.Should().Contain("es-sparkline is-down");
    }

    [Fact]
    public void Sparkline_com_um_ponto_cai_no_fallback_de_linha_reta()
    {
        var (content, _, _) = Render(new EsStatCardTagHelper { Value = "1", Sparkline = "42" });
        content.Should().Contain("M0,18 L120,18");
    }

    [Fact]
    public void Com_href_vira_link_clicavel_is_link()
    {
        var (_, tagName, attrs) = Render(new EsStatCardTagHelper { Label = "A Receber", Value = "R$ 10", Href = "/contas-a-receber" });
        tagName.Should().Be("a");
        attrs.Should().ContainKey("href");
        attrs["href"].Should().Be("/contas-a-receber");
        attrs["class"].Should().Contain("is-link");
    }

    [Fact]
    public void Sem_href_e_div_com_label_e_valor()
    {
        var (content, tagName, _) = Render(new EsStatCardTagHelper { Label = "Saldo do mes", Value = "R$ 1.234,56" });
        tagName.Should().Be("div");
        content.Should().Contain("es-stat-label").And.Contain("Saldo do mes");
        content.Should().Contain("es-stat-value").And.Contain("R$ 1.234,56");
    }

    private static (string content, string tagName, IReadOnlyDictionary<string, string> attrs) Render(EsStatCardTagHelper th)
    {
        var ctx = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test");
        var output = new TagHelperOutput(
            "es-stat-card",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        th.Process(ctx, output);

        using var sw = new StringWriter();
        output.Content.WriteTo(sw, HtmlEncoder.Default);
        var attrs = output.Attributes.ToDictionary(a => a.Name, a => a.Value?.ToString() ?? "");
        return (sw.ToString(), output.TagName ?? "", attrs);
    }
}
