using System.Diagnostics;
using EasyStock.Application.Seguranca;
using EasyStock.Observabilidade;
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

    // #2440: o Npgsql grava a exceção no span (AddException + SetStatus) com a mensagem crua, e o RecordException
    // desligado só vale para o AspNetCore e o HttpClient.
    [Fact]
    public void OnEnd_RedigeADescricaoDoStatus()
    {
        using var span = NovoSpan();
        span.SetStatus(ActivityStatusCode.Error, "falha para o cliente 11987654321");
        span.Stop();

        new SpanQueRedige().OnEnd(span);

        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().NotContain("11987654321").And.Contain(SensitivePatterns.RedactedToken);
    }

    [Fact]
    public void ExcecaoDoNpgsql_SaiRedigidaNoEventoENoStatus()
    {
        ExcecaoDoSpanQueRedige.Instalar("Npgsql");
        using var fonte = new ActivitySource("Npgsql");
        // Quem decide ligar o span é o OpenTelemetry; no teste, um listener de amostragem faz esse papel.
        using var amostra = new ActivityListener
        {
            ShouldListenTo = f => f.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(amostra);

        using var span = fonte.StartActivity("SELECT")!;
        var erro = new InvalidOperationException("cliente 11987654321");
        span.AddException(erro);
        span.SetStatus(ActivityStatusCode.Error, erro.Message);
        span.Stop();

        new SpanQueRedige().OnEnd(span);

        var evento = span.Events.Single(e => e.Name == "exception");
        var tags = evento.Tags.ToDictionary(t => t.Key, t => t.Value?.ToString());
        tags["exception.message"].Should().NotContain("11987654321").And.Contain(SensitivePatterns.RedactedToken);
        tags["exception.stacktrace"].Should().NotContain("11987654321");
        tags["exception.type"].Should().Be(typeof(InvalidOperationException).FullName);
        span.StatusDescription.Should().NotContain("11987654321");
    }

    [Fact]
    public void ListenerDeExcecao_NaoLigaSpanSozinho()
    {
        ExcecaoDoSpanQueRedige.Instalar("Npgsql");
        using var fonte = new ActivitySource("Npgsql");

        using var span = fonte.StartActivity("SELECT");

        span.Should().BeNull();
    }
}
