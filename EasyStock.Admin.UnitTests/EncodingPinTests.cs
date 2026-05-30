using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;

namespace EasyStock.Admin.UnitTests;

/// <summary>
/// Trava o COMPORTAMENTO dos encoders que o fix do stored-XSS (F0) depende:
/// - data-* nas views usa o <c>HtmlEncoder.Default</c> (encoder de atributo do Razor);
/// - Diagnostico/Index.cshtml usa <c>JsonSerializer.Serialize</c> (encoder default escapa &lt;).
/// Se alguém trocar o encoder (ex.: UnsafeRelaxedJsonEscaping) ou o comportamento mudar,
/// estes testes quebram antes de reintroduzir o XSS.
/// </summary>
public class EncodingPinTests
{
    // Nome de usuário/loja controlado por tenant — o vetor real do F0.
    private const string AttributeBreakoutPayload = "a\" onmouseover=\"alert(document.cookie)\" x=\"";
    private const string ScriptBreakoutPayload = "</script><img src=x onerror=alert(1)>";

    [Fact]
    public void HtmlEncoder_default_neutraliza_breakout_de_atributo()
    {
        // É o que `data-unome="@unome"` emite. A aspa dupla DEVE virar entidade.
        var encoded = HtmlEncoder.Default.Encode(AttributeBreakoutPayload);

        encoded.Should().NotContain("\"", "a aspa dupla crua quebraria o atributo data-*");
        encoded.Should().Contain("&quot;", "a aspa dupla deve virar entidade HTML");
        encoded.Should().NotContain("onmouseover=\"", "o handler injetado não pode sobreviver como atributo real");
    }

    [Theory]
    [InlineData("João's")]
    [InlineData("Café Central")]
    [InlineData("R&D <Loja>")]
    [InlineData("aspas \"literais\" e 'simples'")]
    public void HtmlEncoder_default_faz_roundtrip_de_nome_legitimo(string nome)
    {
        // Garante que o fix não troca XSS por bug: nome legítimo volta idêntico
        // (sem virar "João's" visível, que era o risco do escape JS manual).
        var roundTrip = WebUtility.HtmlDecode(HtmlEncoder.Default.Encode(nome));
        roundTrip.Should().Be(nome);
    }

    [Fact]
    public void JsonSerializer_default_escapa_anglebracket_e_impede_breakout_de_script()
    {
        // É o que `data: @Html.Raw(JsonSerializer.Serialize(Model.StatusData))` emite
        // dentro de <script>. </script> embutido NÃO pode aparecer cru.
        var json = JsonSerializer.Serialize(ScriptBreakoutPayload);

        json.Should().NotContain("</", "</script> cru terminaria o bloco <script> e injetaria HTML");
        json.Should().Contain("\\u003C", "o < deve ser escapado como \\u003C pelo encoder default");
    }

    [Fact]
    public void JsonSerializer_relaxado_seria_inseguro_documenta_a_dependencia()
    {
        // Pin explícito (item 5 da review): trocar pro encoder relaxado RE-ABRE o vetor.
        // Este teste documenta o porquê de NÃO usar UnsafeRelaxedJsonEscaping nas views.
        var inseguro = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var json = JsonSerializer.Serialize(ScriptBreakoutPayload, inseguro);

        json.Should().Contain("</script>", "comprova que o encoder relaxado deixaria passar o breakout");
    }
}
