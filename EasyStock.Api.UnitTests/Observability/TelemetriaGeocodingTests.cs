using System.Collections.Concurrent;
using System.Diagnostics;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Infra.Integrations.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace EasyStock.Api.UnitTests.Observability;

/// <summary>
/// Endereço e CEP fora do span e do log (I8.2, #1661). Os adapters de CEP e de geocodificação põem o
/// dado do cliente na URL; aqui eles rodam de verdade (HttpClientFactory + instrumentação do OTel)
/// contra um servidor local, e nada do que sai para span ou log pode conter o endereço-canário.
/// </summary>
public sealed class TelemetriaGeocodingTests : IAsyncLifetime
{
    private const string CepCanario = "01234987";
    private const string RuaCanario = "Canario Amarelo";
    private const string NumeroCanario = "4242";
    private static readonly string[] Canarios = [CepCanario, RuaCanario, "Canario", NumeroCanario];

    private WebApplication _provedor = null!;
    private string _base = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _provedor = builder.Build();
        // #1680: o servidor demora mais que o timeout de produção do cliente de CEP (1 s) de propósito.
        // O teste não pode depender desse prazo (ver PrazoLargoSoNoTeste): no CI a primeira chamada,
        // fria, passou de 1 s, o cliente devolveu nulo e o teste falhou de vez em quando.
        _provedor.MapGet("/viacep/ws/{cep}/json/", async () =>
        {
            await Task.Delay(AtrasoDoProvedor);
            return Results.Json(new
            {
                logradouro = "Rua " + RuaCanario, bairro = "Centro", localidade = "São Paulo", uf = "SP",
            });
        });
        _provedor.MapGet("/nominatim/search", () => Results.Json(new[]
        {
            new { lat = "-23.5615", lon = "-46.6560", addresstype = "house", address = new { house_number = NumeroCanario } },
        }));
        await _provedor.StartAsync();
        _base = _provedor.Urls.Single();
    }

    public async Task DisposeAsync() => await _provedor.DisposeAsync();

    private sealed class LogCapturado : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Linhas { get; } = new();
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Linhas.Enqueue($"{formatter(state, exception)} {exception}");
        public void Dispose() { }
    }

    private static readonly TimeSpan AtrasoDoProvedor = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Os adapters registram timeout de produção (1 s no CEP, 2 s na geocodificação). Aqui o que se mede é
    /// o que vai para span e log, não o prazo: registrada por último, esta ação vale sobre a do adapter.
    /// </summary>
    private static void PrazoLargoSoNoTeste(IServiceCollection services) =>
        services.ConfigureAll<HttpClientFactoryOptions>(o =>
            o.HttpClientActions.Add(client => client.Timeout = TimeSpan.FromSeconds(30)));

    /// <summary>Roda um CEP e uma geocodificação e devolve tudo o que foi para span e para log.</summary>
    private async Task<(List<string> Spans, List<string> Logs)> ExercitarAsync(bool protegido)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CepServiceCollectionExtensions.FeatureFlagKey] = "true",
            ["Storefront:Frete:ViaCepBaseUrl"] = _base + "/viacep/",
            [GeocodingServiceCollectionExtensions.FeatureFlagKey] = "true",
            ["Storefront:Frete:NominatimBaseUrl"] = _base + "/nominatim/",
        }).Build();

        var log = new LogCapturado();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(log));
        services.AddEasyStockCepLookup(configuration);
        services.AddEasyStockGeocoding(configuration);
        if (protegido) services.AddEnderecoForaDoLogHttp();
        PrazoLargoSoNoTeste(services);

        var spans = new List<Activity>();
        using (var tracer = Sdk.CreateTracerProviderBuilder()
                   .AddHttpClientInstrumentation(o =>
                   {
                       if (protegido) o.EnrichWithHttpRequestMessage = EnderecoForaDaTelemetria.OcultarUrlNoSpan(configuration);
                   })
                   .AddInMemoryExporter(spans)
                   .Build())
        await using (var sp = services.BuildServiceProvider())
        await using (var escopo = sp.CreateAsyncScope())
        {
            var endereco = await escopo.ServiceProvider.GetRequiredService<ICepLookupClient>().LookupAsync(CepCanario);
            endereco!.Logradouro.Should().Contain(RuaCanario, "o servidor local respondeu o CEP");

            var geo = await escopo.ServiceProvider.GetRequiredService<IGeocodingClient>().GeocodificarAsync(
                new GeocodeQuery(endereco.Logradouro, NumeroCanario, endereco.Bairro, endereco.Cidade, endereco.Uf, CepCanario));
            geo!.Confiavel.Should().BeTrue("o servidor local respondeu a geocodificação");

            tracer.ForceFlush();
        }

        // A instrumentação é do processo: outros testes em paralelo também geram span de HttpClient.
        var porta = new Uri(_base).Port;
        spans = [.. spans.Where(a => Equals(a.GetTagItem("server.port"), porta))];
        spans.Should().HaveCount(2, "uma chamada de CEP e uma de geocodificação");
        var textoDosSpans = spans.Select(a =>
            a.DisplayName + " " + string.Join(" ", a.TagObjects.Select(t => $"{t.Key}={Uri.UnescapeDataString(t.Value?.ToString() ?? "")}"))).ToList();
        return (textoDosSpans, [.. log.Linhas.Select(Uri.UnescapeDataString)]);
    }

    [Fact]
    public async Task Endereco_canario_nao_aparece()
    {
        var (spans, logs) = await ExercitarAsync(protegido: true);

        foreach (var canario in Canarios)
        {
            spans.Should().NotContain(s => s.Contains(canario, StringComparison.OrdinalIgnoreCase), $"'{canario}' não pode ir para o span");
            logs.Should().NotContain(l => l.Contains(canario, StringComparison.OrdinalIgnoreCase), $"'{canario}' não pode ir para o log");
        }
    }

    [Fact]
    public async Task O_span_continua_dizendo_qual_provedor_foi_chamado()
    {
        var (spans, _) = await ExercitarAsync(protegido: true);

        spans.Should().OnlyContain(s => s.Contains("url.full=" + _base + "/"), "host e porta seguem no span para diagnóstico");
        spans.Should().OnlyContain(s => s.Contains("http.response.status_code=200"));
    }

    /// <summary>Contraprova: sem a proteção o canário vaza. Se isto parar de falhar, o teste acima deixou de enxergar.</summary>
    [Fact]
    public async Task Sem_a_protecao_o_canario_vaza_no_span_e_no_log()
    {
        var (spans, logs) = await ExercitarAsync(protegido: false);

        spans.Should().Contain(s => s.Contains(CepCanario), "o CEP vai no caminho da URL do ViaCEP");
        logs.Should().Contain(l => l.Contains(CepCanario), "o HttpClientFactory loga a URL da requisição");
    }
}
