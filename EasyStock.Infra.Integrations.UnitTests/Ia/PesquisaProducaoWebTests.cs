using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Infra.Integrations.Ia;
using FluentAssertions;

namespace EasyStock.Infra.Integrations.UnitTests.Ia;

public class PesquisaProducaoWebTests
{
    [Fact]
    public async Task Insumos_escapam_termo_e_mantem_host_publico_fixo()
    {
        const string termo = "ovo&action=edit https://127.0.0.1/segredo";
        using var resposta = Resposta("""["ovo",["Ovo","Farinha","Leite","Açúcar"],[],[]]""");
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(resposta));
        using var http = new HttpClient(handler);
        var antes = DateTimeOffset.UtcNow;

        var fontes = await new PesquisaProducaoWeb(http).BuscarInsumosAsync(termo, CancellationToken.None);

        handler.Uris.Should().ContainSingle();
        handler.Uris[0].Host.Should().Be("pt.wikipedia.org");
        handler.Uris[0].Query.Should().Contain("search=" + Uri.EscapeDataString(termo));
        fontes.Select(f => f.Titulo).Should().Equal("Ovo", "Farinha", "Leite");
        fontes.Should().OnlyContain(f => f.Url.StartsWith("https://pt.wikipedia.org/wiki/", StringComparison.Ordinal) && f.Texto == "");
        fontes.Should().OnlyContain(f => f.ConsultadaEm >= antes && f.ConsultadaEm <= DateTimeOffset.UtcNow);
        resposta.Descartada.Should().BeTrue();
    }

    [Fact]
    public async Task Receitas_filtram_titulos_e_limitam_extrato_sem_buscar_links_arbitrarios()
    {
        const string titulo = "Livro de receitas/https://127.0.0.1 Bolo";
        var respostas = new Queue<RespostaRastreada>();
        respostas.Enqueue(Resposta(JsonSerializer.Serialize(new object[] { "bolo", new[] { "Categoria:Bolos", titulo }, Array.Empty<string>(), new[] { "http://127.0.0.1/segredo" } })));
        respostas.Enqueue(Resposta(JsonSerializer.Serialize(new { query = new { pages = new[] { new { extract = new string('x', 7000) } } } })));
        var rastreadas = respostas.ToArray();
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(respostas.Dequeue()));
        using var http = new HttpClient(handler);

        var fontes = await new PesquisaProducaoWeb(http).BuscarReceitasAsync("Bolo", CancellationToken.None);

        fontes.Should().ContainSingle();
        fontes[0].Titulo.Should().Be(titulo);
        fontes[0].Texto.Should().HaveLength(6000);
        fontes[0].Url.Should().Be("https://pt.wikibooks.org/wiki/" + Uri.EscapeDataString(titulo.Replace(' ', '_')));
        handler.Uris.Should().HaveCount(2).And.OnlyContain(u => u.Scheme == "https" && u.Host == "pt.wikibooks.org" && u.AbsolutePath == "/w/api.php");
        handler.Uris[1].Query.Should().Contain("titles=" + Uri.EscapeDataString(titulo));
        rastreadas.Should().OnlyContain(r => r.Descartada);
    }

    [Theory]
    [InlineData("{malformado")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[\"ovo\",{},[],[]]")]
    [InlineData("[\"ovo\",[17],[],[]]")]
    public async Task Busca_com_json_invalido_e_rejeitada_com_erro_de_fonte(string json)
    {
        using var resposta = Resposta(json);
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(resposta));
        using var http = new HttpClient(handler);
        var acao = () => new PesquisaProducaoWeb(http).BuscarInsumosAsync("Ovo", CancellationToken.None);
        await acao.Should().ThrowAsync<JsonException>();
        resposta.Descartada.Should().BeTrue();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"query\":[]}")]
    [InlineData("{\"query\":{\"pages\":{}}}")]
    [InlineData("{\"query\":{\"pages\":[{\"extract\":17}]}}")]
    public async Task Extracts_com_formato_invalido_e_rejeitado_com_json_exception(string json)
    {
        var respostas = new Queue<RespostaRastreada>([Resposta("""["bolo",["Livro de receitas/Bolo"],[],[]]"""), Resposta(json)]);
        var rastreadas = respostas.ToArray();
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(respostas.Dequeue()));
        using var http = new HttpClient(handler);
        var acao = () => new PesquisaProducaoWeb(http).BuscarReceitasAsync("Bolo", CancellationToken.None);
        await acao.Should().ThrowAsync<JsonException>();
        rastreadas.Should().OnlyContain(r => r.Descartada);
    }

    [Theory]
    [InlineData("{\"query\":{\"pages\":[]}}")]
    [InlineData("{\"query\":{\"pages\":[{\"missing\":true}]}}")]
    [InlineData("{\"query\":{\"pages\":[{\"extract\":\"  \"}]}}")]
    public async Task Pagina_sem_texto_nao_vira_evidencia(string json)
    {
        var respostas = new Queue<RespostaRastreada>([Resposta("""["bolo",["Livro de receitas/Bolo"],[],[]]"""), Resposta(json)]);
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(respostas.Dequeue()));
        using var http = new HttpClient(handler);
        (await new PesquisaProducaoWeb(http).BuscarReceitasAsync("Bolo", CancellationToken.None)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Http_sem_sucesso_nao_retorna_fontes_e_descarta_corpo(HttpStatusCode status)
    {
        using var resposta = Resposta("{}", status);
        resposta.Headers.Location = new Uri("http://127.0.0.1/segredo");
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(resposta));
        using var http = new HttpClient(handler);
        var acao = () => new PesquisaProducaoWeb(http).BuscarInsumosAsync("Ovo", CancellationToken.None);
        await acao.Should().ThrowAsync<HttpRequestException>();
        handler.Uris.Should().ContainSingle();
        resposta.Descartada.Should().BeTrue();
    }

    [Theory]
    [InlineData(262144)]
    [InlineData(262145)]
    public async Task Limite_de_bytes_e_aplicado_mesmo_sem_content_length(int tamanho)
    {
        const string json = "[\"ovo\",[],[],[]]";
        using var stream = new StreamRastreado(Encoding.UTF8.GetBytes(json + new string(' ', tamanho - Encoding.UTF8.GetByteCount(json))));
        using var resposta = new RespostaRastreada(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(resposta));
        using var http = new HttpClient(handler);
        var acao = () => new PesquisaProducaoWeb(http).BuscarInsumosAsync("Ovo", CancellationToken.None);
        if (tamanho > 262144) await acao.Should().ThrowAsync<HttpRequestException>();
        else (await acao()).Should().BeEmpty();
        stream.Descartado.Should().BeTrue();
        resposta.Descartada.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelamento_do_chamador_interrompe_corpo_e_descarta_recursos()
    {
        using var cts = new CancellationTokenSource();
        using var stream = new StreamBloqueado();
        using var resposta = new RespostaRastreada(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(resposta));
        using var http = new HttpClient(handler);
        var tarefa = new PesquisaProducaoWeb(http).BuscarInsumosAsync("Ovo", cts.Token);
        await stream.LeituraIniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        var acao = () => tarefa.WaitAsync(TimeSpan.FromSeconds(5));
        await acao.Should().ThrowAsync<OperationCanceledException>();
        stream.Descartado.Should().BeTrue();
        resposta.Descartada.Should().BeTrue();
    }

    [Fact]
    public async Task Deadline_integral_interrompe_corpo_apos_cabecalhos()
    {
        using var stream = new StreamBloqueado();
        using var resposta = new RespostaRastreada(HttpStatusCode.OK) { Content = new StreamContent(stream) };
        using var handler = new Handler((_, _) => Task.FromResult<HttpResponseMessage>(resposta));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };
        var acao = () => new PesquisaProducaoWeb(http).BuscarInsumosAsync("Ovo", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
        await acao.Should().ThrowAsync<OperationCanceledException>();
        stream.LeituraIniciada.Task.IsCompletedSuccessfully.Should().BeTrue();
        stream.Descartado.Should().BeTrue();
        resposta.Descartada.Should().BeTrue();
    }

    private static RespostaRastreada Resposta(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> enviar) : HttpMessageHandler
    {
        public List<Uri> Uris { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!);
            return enviar(request, cancellationToken);
        }
    }

    private sealed class RespostaRastreada(HttpStatusCode status) : HttpResponseMessage(status)
    {
        public bool Descartada { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) Descartada = true; base.Dispose(disposing); }
    }

    private sealed class StreamRastreado(byte[] dados) : MemoryStream(dados)
    {
        public bool Descartado { get; private set; }
        public override bool CanSeek => false;
        protected override void Dispose(bool disposing) { if (disposing) Descartado = true; base.Dispose(disposing); }
    }

    private sealed class StreamBloqueado : MemoryStream
    {
        public TaskCompletionSource LeituraIniciada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Descartado { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            LeituraIniciada.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        protected override void Dispose(bool disposing) { if (disposing) Descartado = true; base.Dispose(disposing); }
    }
}
