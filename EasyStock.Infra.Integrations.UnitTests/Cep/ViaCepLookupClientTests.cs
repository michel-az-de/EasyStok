using System.Net;
using EasyStock.Infra.Integrations.Cep;
using EasyStock.TestHelpers.Http;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.UnitTests.Cep;

/// <summary>
/// Adapter do ViaCEP (I8.2, #1661): achou, não achou e falhou. O contrato é nunca lançar, porque o
/// checkout segue pelo CEP quando o endereço não vem. HTTP por <see cref="HttpFalso"/>, sem rede.
/// </summary>
public class ViaCepLookupClientTests
{
    private const string Cep = "01310100";
    private const string Rota = "/ws/01310100/json/";

    private readonly HttpFalso _http = new();

    private readonly LogEmMemoria _log = new();

    private sealed class LogEmMemoria : ILogger<ViaCepLookupClient>
    {
        public List<string> Linhas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Linhas.Add($"{formatter(state, exception)} {exception}");
    }

    private ViaCepLookupClient Cliente(TimeSpan? timeout = null)
    {
        var client = _http.CriarClient("https://viacep.test/");
        if (timeout is { } t) client.Timeout = t;
        return new ViaCepLookupClient(client, _log);
    }

    [Fact]
    public async Task Falha_nao_escreve_o_cep_no_log()
    {
        _http.Quando(HttpMethod.Get, Rota)
            .Responde(HttpStatusCode.InternalServerError)
            .Lanca(new HttpRequestException("sem rota"))
            .Responde(HttpStatusCode.OK, "<html>fora do ar</html>")
            .Demora(TimeSpan.FromSeconds(10));

        for (var i = 0; i < 3; i++) await Cliente().LookupAsync(Cep);
        await Cliente(TimeSpan.FromMilliseconds(50)).LookupAsync(Cep);

        Assert.Equal(4, _log.Linhas.Count);
        Assert.DoesNotContain(_log.Linhas, l => l.Contains(Cep));
    }

    [Fact]
    public async Task Cep_existente_devolve_o_endereco()
    {
        _http.Quando(HttpMethod.Get, Rota).Responde(HttpStatusCode.OK,
            """{"cep":"01310-100","logradouro":"Avenida Paulista","bairro":"Bela Vista","localidade":"São Paulo","uf":"SP"}""");

        var r = await Cliente().LookupAsync(Cep);

        Assert.NotNull(r);
        Assert.Equal(Cep, r!.Cep);
        Assert.Equal("Avenida Paulista", r.Logradouro);
        Assert.Equal("Bela Vista", r.Bairro);
        Assert.Equal("São Paulo", r.Cidade);
        Assert.Equal("SP", r.Uf);
    }

    [Fact]
    public async Task Cep_inexistente_devolve_vazio()
    {
        // ViaCEP responde 200 com {"erro":true} para CEP que não existe.
        _http.Quando(HttpMethod.Get, Rota).Responde(HttpStatusCode.OK, """{"erro":true}""");

        Assert.Null(await Cliente().LookupAsync(Cep));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Erro_http_devolve_vazio(HttpStatusCode status)
    {
        _http.Quando(HttpMethod.Get, Rota).Responde(status);

        Assert.Null(await Cliente().LookupAsync(Cep));
    }

    [Fact]
    public async Task Timeout_devolve_vazio_sem_lancar()
    {
        _http.Quando(HttpMethod.Get, Rota).Demora(TimeSpan.FromSeconds(10));

        Assert.Null(await Cliente(TimeSpan.FromMilliseconds(50)).LookupAsync(Cep));
    }

    [Fact]
    public async Task Falha_de_rede_devolve_vazio_sem_lancar()
    {
        _http.Quando(HttpMethod.Get, Rota).Lanca(new HttpRequestException("sem rota"));

        Assert.Null(await Cliente().LookupAsync(Cep));
    }

    [Theory]
    [InlineData("<html>fora do ar</html>")]
    [InlineData("null")]
    public async Task Corpo_que_nao_e_o_json_esperado_devolve_vazio(string corpo)
    {
        _http.Quando(HttpMethod.Get, Rota).Responde(HttpStatusCode.OK, corpo);

        Assert.Null(await Cliente().LookupAsync(Cep));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task Cep_em_branco_nao_bate_na_rede(string? cep)
    {
        Assert.Null(await Cliente().LookupAsync(cep!));
        Assert.Empty(_http.Chamadas);
    }

    [Fact]
    public async Task Cancelamento_do_chamador_propaga()
    {
        _http.Quando(HttpMethod.Get, Rota).Demora(TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cliente().LookupAsync(Cep, cts.Token));
    }
}
