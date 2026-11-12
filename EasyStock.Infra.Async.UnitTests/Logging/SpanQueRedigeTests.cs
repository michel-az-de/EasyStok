using System.Diagnostics;
using EasyStock.Application.Seguranca;
using EasyStock.Infra.Async.Logging;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Logging;

/// <summary>
/// #2376: o span chega ao exporter com o mesmo mascaramento do log. Antes, a redação valia só para o Serilog e o trace
/// levava caminho, URL de integração e SQL em claro ao OpenObserve.
/// </summary>
[Trait("Category", "SecurityRegression")]
public class SpanQueRedigeTests
{
    private static Activity NovoSpan()
    {
        var span = new Activity("teste");
        span.Start();
        return span;
    }

    [Fact]
    public void OnEnd_RedigeTagsDeTexto()
    {
        using var span = NovoSpan();
        span.SetTag("url.path", "/api/atendimento/conversas/11987654321/mensagens");
        span.SetTag("url.full", "https://waha.local/api/sendText?token=segredo123");
        span.SetTag("db.query.text", "select * from x where email = 'ana@exemplo.com'");
        span.Stop();

        new SpanQueRedige().OnEnd(span);

        span.GetTagItem("url.path").Should().Be("/api/atendimento/conversas/[REDACTED]/mensagens");
        span.GetTagItem("url.full").As<string>().Should().NotContain("segredo123");
        span.GetTagItem("db.query.text").As<string>().Should().NotContain("ana@exemplo.com");
    }

    [Fact]
    public void OnEnd_PreservaTagSemSegredoENaoTexto()
    {
        using var span = NovoSpan();
        span.SetTag("http.route", "/api/pedidos/{id}");
        span.SetTag("http.response.status_code", 200);
        span.Stop();

        new SpanQueRedige().OnEnd(span);

        span.GetTagItem("http.route").Should().Be("/api/pedidos/{id}");
        span.GetTagItem("http.response.status_code").Should().Be(200);
    }

    [Fact]
    public void OnEnd_RedigeNomeDoSpan()
    {
        using var span = NovoSpan();
        span.DisplayName = "GET /api/clientes/cpf/12345678909";
        span.Stop();

        new SpanQueRedige().OnEnd(span);

        span.DisplayName.Should().NotContain("12345678909").And.Contain(SensitivePatterns.RedactedToken);
    }
}
