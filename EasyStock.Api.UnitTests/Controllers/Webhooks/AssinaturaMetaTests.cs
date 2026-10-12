using EasyStock.Api.Controllers.Webhooks;
using EasyStock.TestHelpers.Webhooks;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Controllers.Webhooks;

/// <summary>
/// #1634: a fábrica <see cref="AssinaturaMeta"/> é conferida contra o validador real
/// (<see cref="AssinaturaWebhookMeta"/>): o que ela gera passa, e um caractere trocado no corpo ou no segredo não passa.
/// </summary>
public class AssinaturaMetaTests
{
    private const string AppSecret = "app-secret-de-teste";
    private const string Corpo = """{"object":"whatsapp_business_account","entry":[{"id":"1","changes":[]}]}""";

    [Fact]
    public void AssinaturaGerada_EAceitaPeloValidadorReal()
    {
        var cabecalho = AssinaturaMeta.Gerar(Corpo, AppSecret);

        cabecalho.Should().MatchRegex("^sha256=[0-9a-f]{64}$");
        AssinaturaWebhookMeta.Valida(cabecalho, Corpo, AppSecret).Should().BeTrue();
    }

    [Fact]
    public void CorpoComAcento_EAssinadoEmUtf8()
    {
        const string corpoComAcento = """{"text":{"body":"Olá, Casa da Babá"}}""";

        var cabecalho = AssinaturaMeta.Gerar(corpoComAcento, AppSecret);

        AssinaturaWebhookMeta.Valida(cabecalho, corpoComAcento, AppSecret).Should().BeTrue();
    }

    [Fact]
    public void CorpoAdulterado_ERecusado()
    {
        var cabecalho = AssinaturaMeta.Gerar(Corpo, AppSecret);

        AssinaturaWebhookMeta.Valida(cabecalho, Corpo.Replace("\"1\"", "\"2\""), AppSecret).Should().BeFalse();
    }

    [Fact]
    public void SegredoAdulterado_ERecusado()
    {
        var cabecalho = AssinaturaMeta.Gerar(Corpo, AppSecret + "x");

        AssinaturaWebhookMeta.Valida(cabecalho, Corpo, AppSecret).Should().BeFalse();
    }

    [Fact]
    public void NomeDoCabecalho_EOMesmoDoValidador()
    {
        AssinaturaMeta.Header.Should().Be(AssinaturaWebhookMeta.Header);
    }
}
