using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Guard (I4.5, #1698; invariante 16): nenhum sink do Serilog fica sem redação, na API ou no Worker. Antes só o
/// arquivo da API redigia; o console é o log do contêiner em produção e o OTLP sai da máquina. Um sink novo
/// colado direto em <c>WriteTo</c> voltaria a vazar sem nenhum teste de unidade perceber.
///
/// Source-text based (idioma de <see cref="ChaveMestraNosDoisHostsTests"/>).
/// </summary>
[Trait("Category", "Architecture")]
public class TodoSinkRedigeTests
{
    private static readonly string[] SinksQueRedigem = ["Redigido", "RedactedFile"];
    private static readonly Regex ComentarioDeLinha = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    [Theory]
    [InlineData("EasyStock.Api/appsettings.json")]
    [InlineData("EasyStock.Api/appsettings.Production.json")]
    [InlineData("EasyStock.Api/appsettings.Development.json")]
    [InlineData("EasyStock.Worker/appsettings.json")]
    public void Sink_do_appsettings_redige(string arquivo)
    {
        using var json = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), arquivo)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        if (!json.RootElement.TryGetProperty("Serilog", out var serilog) || !serilog.TryGetProperty("WriteTo", out var sinks))
            return;

        var nomes = sinks.EnumerateArray().Select(s => s.GetProperty("Name").GetString()).ToList();
        nomes.Should().OnlyContain(n => SinksQueRedigem.Contains(n),
            "sink fora do Redigido escreve segredo em claro; envolva-o: {{ \"Name\": \"Redigido\", \"Args\": {{ \"configure\": [ ... ] }} }}");
    }

    [Theory]
    [InlineData("EasyStock.Api/Program.cs")]
    [InlineData("EasyStock.Worker/Program.cs")]
    public void Sink_ligado_no_codigo_redige(string program)
    {
        var codigo = ComentarioDeLinha.Replace(File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), program)), string.Empty);

        var diretos = Regex.Matches(codigo, @"\.WriteTo\s*\.\s*(\w+)\s*\(").Select(m => m.Groups[1].Value).ToList();
        diretos.Should().OnlyContain(n => SinksQueRedigem.Contains(n),
            "use WriteTo.Redigido(w => w.<Sink>(...)) para o sink receber o evento ja redigido");
    }

    // #2376: o trace também sai da máquina. Todo WithTracing que exporta por OTLP registra o SpanQueRedige antes.
    // #2390: o console Web entrou, pelo EasyStock.Observabilidade.
    [Theory]
    [InlineData("EasyStock.Api/Configuration/ApiServiceCollectionExtensions.cs")]
    [InlineData("EasyStock.Worker/DependencyInjection/TelemetrySetup.cs")]
    [InlineData("EasyStock.Web/Infrastructure/TelemetrySetup.cs")]
    public void Trace_exportado_passa_pelo_SpanQueRedige(string arquivo)
    {
        var codigo = ComentarioDeLinha.Replace(File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), arquivo)), string.Empty);
        var tracing = codigo[codigo.IndexOf(".WithTracing(", StringComparison.Ordinal)..codigo.IndexOf(".WithMetrics(", StringComparison.Ordinal)];

        var processador = tracing.IndexOf(".AddProcessor(new SpanQueRedige())", StringComparison.Ordinal);
        var exporter = tracing.IndexOf(".AddOtlpExporter(", StringComparison.Ordinal);
        exporter.Should().BeGreaterThan(-1, "o bloco de tracing exporta por OTLP");
        processador.Should().BeInRange(0, exporter,
            "registre .AddProcessor(new SpanQueRedige()) antes do .AddOtlpExporter: processador roda na ordem do registro");
    }

    // #2390: o Web manda o ILogger direto ao OpenTelemetry, sem Serilog. O LogQueRedige roda antes do exporter, e o
    // escopo (não editável pelo processador) fica desligado.
    [Fact]
    public void Log_OTLP_do_Web_passa_pelo_LogQueRedige()
    {
        var codigo = ComentarioDeLinha.Replace(
            File.ReadAllText(Path.Combine(RepoPaths.FindRepoRoot(), "EasyStock.Web/Infrastructure/TelemetrySetup.cs")), string.Empty);
        var logging = codigo[codigo.IndexOf(".Logging.AddOpenTelemetry(", StringComparison.Ordinal)..];

        var processador = logging.IndexOf(".AddProcessor(new LogQueRedige())", StringComparison.Ordinal);
        var exporter = logging.IndexOf(".AddOtlpExporter(", StringComparison.Ordinal);
        exporter.Should().BeGreaterThan(-1, "o bloco de logs exporta por OTLP");
        processador.Should().BeInRange(0, exporter,
            "registre .AddProcessor(new LogQueRedige()) antes do .AddOtlpExporter: processador roda na ordem do registro");
        logging.Should().NotMatchRegex(@"IncludeScopes\s*=\s*true",
            "o escopo do ILogger não passa pelo LogQueRedige e levaria o RequestPath cru");
    }
}
