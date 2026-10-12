using System.Net;
using System.Text;
using EasyStock.TestHelpers.Http;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.KitDeTeste;

/// <summary>
/// #1634: o <see cref="HttpFalso"/> é o handler HTTP único dos testes de adapter de terceiros. Estes testes
/// travam o contrato dele (rota, gravação da chamada, sequência, exceção, atraso e rota não prevista), porque
/// um fake que mente derruba a confiança de todo teste que o usa.
/// </summary>
public class HttpFalsoTests
{
    private const string Base = "https://provedor.test/";

    [Fact]
    public async Task RotaRegistrada_RespondeStatusCorpoECabecalho()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Get, "/v1/pedidos/7").Responde(
            HttpStatusCode.Accepted, """{"id":7}""", new Dictionary<string, string> { ["Retry-After"] = "3" });
        using var http = falso.CriarClient(Base);

        using var resposta = await http.GetAsync("v1/pedidos/7");

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await resposta.Content.ReadAsStringAsync()).Should().Be("""{"id":7}""");
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        resposta.Headers.GetValues("Retry-After").Should().Equal("3");
    }

    [Fact]
    public async Task CaminhoCasaSemAQuery_EMetodoDiferenteNaoCasa()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Get, "v1/busca").Responde(HttpStatusCode.OK, "[]");
        using var http = falso.CriarClient(Base);

        using var resposta = await http.GetAsync("v1/busca?q=bolo&pagina=2");
        var post = () => http.PostAsync("v1/busca", new StringContent("{}"));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        await post.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Predicado_CasaPelaQuery()
    {
        var falso = new HttpFalso();
        falso.Quando(r => r.RequestUri!.Query.Contains("status=pago"), "status=pago").Responde(HttpStatusCode.OK, """{"pago":true}""");
        falso.Quando(HttpMethod.Get, "/v1/cobrancas").Responde(HttpStatusCode.OK, """{"pago":false}""");
        using var http = falso.CriarClient(Base);

        var pago = await http.GetStringAsync("v1/cobrancas?status=pago");
        var aberto = await http.GetStringAsync("v1/cobrancas?status=aberto");

        pago.Should().Be("""{"pago":true}""");
        aberto.Should().Be("""{"pago":false}""");
    }

    [Fact]
    public async Task GravaMetodoUrlCabecalhoECorpo()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Post, "/v1/pagamentos").Responde(HttpStatusCode.Created, "{}");
        using var http = falso.CriarClient(Base);
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "v1/pagamentos?origem=teste")
        {
            Content = new StringContent("""{"valor":10.5}""", Encoding.UTF8, "application/json"),
        };
        requisicao.Headers.Add("X-Idempotency-Key", "chave-1");

        // O corpo é lido na hora da chamada: aqui a requisição e o conteúdo já foram descartados.
        (await http.SendAsync(requisicao)).Dispose();
        requisicao.Dispose();

        falso.Chamadas.Should().ContainSingle();
        var chamada = falso.UltimaChamada!;
        chamada.Metodo.Should().Be(HttpMethod.Post);
        chamada.Uri.AbsoluteUri.Should().Be("https://provedor.test/v1/pagamentos?origem=teste");
        chamada.Cabecalho("x-idempotency-key").Should().Be("chave-1");
        chamada.Cabecalhos["Content-Type"].Should().ContainSingle().Which.Should().StartWith("application/json");
        chamada.Cabecalho("X-Nao-Enviado").Should().BeNull();
        chamada.Corpo.Should().Be("""{"valor":10.5}""");
    }

    [Fact]
    public async Task SemChamada_UltimaChamadaENulaEGetSemCorpoGravaNulo()
    {
        var falso = new HttpFalso();
        falso.QuandoQualquer().Responde(HttpStatusCode.OK, "{}");
        using var http = falso.CriarClient(Base);

        falso.Chamadas.Should().BeEmpty();
        falso.UltimaChamada.Should().BeNull();

        await http.GetStringAsync("a");
        await http.GetStringAsync("b");

        falso.Chamadas.Select(c => c.Uri.AbsolutePath).Should().Equal("/a", "/b");
        falso.UltimaChamada!.Corpo.Should().BeNull();
    }

    [Fact]
    public async Task Sequencia_RespondeNaOrdemERepeteAUltima()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Get, "/v1/instavel")
            .Responde(HttpStatusCode.InternalServerError, "{}")
            .Responde(HttpStatusCode.OK, """{"ok":true}""");
        using var http = falso.CriarClient(Base);

        using var primeira = await http.GetAsync("v1/instavel");
        using var segunda = await http.GetAsync("v1/instavel");
        using var terceira = await http.GetAsync("v1/instavel");

        primeira.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        terceira.StatusCode.Should().Be(HttpStatusCode.OK, "esgotada a sequência, a última resposta se repete");
        falso.Chamadas.Should().HaveCount(3);
    }

    [Fact]
    public async Task Lanca_PropagaAExcecaoConfiguradaEGravaAChamada()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Get, "/v1/fora-do-ar").Lanca(new HttpRequestException("conexão recusada"));
        using var http = falso.CriarClient(Base);

        var act = () => http.GetAsync("v1/fora-do-ar");

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Be("conexão recusada");
        falso.Chamadas.Should().ContainSingle();
    }

    [Fact]
    public async Task Lanca_TaskCanceledSimulaTimeout_EDepoisResponde()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Get, "/v1/lento")
            .Lanca(new TaskCanceledException("timeout simulado"))
            .Responde(HttpStatusCode.OK, "{}");
        using var http = falso.CriarClient(Base);

        var primeira = () => http.GetAsync("v1/lento");
        await primeira.Should().ThrowAsync<TaskCanceledException>();
        using var segunda = await http.GetAsync("v1/lento");

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RotaNaoPrevista_LancaComMetodoUrlERotasRegistradas()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Get, "/v1/pedidos").Responde(HttpStatusCode.OK, "[]");
        falso.Quando(r => r.Headers.Contains("X-Teste"), "com cabeçalho X-Teste").Responde(HttpStatusCode.OK, "{}");
        using var http = falso.CriarClient(Base);

        var act = () => http.DeleteAsync("v1/pedidos/9?forcar=1");

        var erro = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        erro.Message.Should().Contain("DELETE")
            .And.Contain("https://provedor.test/v1/pedidos/9?forcar=1")
            .And.Contain("GET /v1/pedidos")
            .And.Contain("com cabeçalho X-Teste");
        falso.Chamadas.Should().ContainSingle("a chamada não prevista também fica gravada");
    }

    [Fact]
    public async Task SemRotaNenhuma_AMensagemDizQueNaoHaRotas()
    {
        var falso = new HttpFalso();
        using var http = falso.CriarClient(Base);

        var act = () => http.GetAsync("qualquer");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("nenhuma rota registrada");
    }

    [Fact]
    public async Task Demora_RespeitaOCancelamento()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Get, "/v1/lento").Demora(TimeSpan.FromSeconds(30), HttpStatusCode.OK, "{}");
        using var http = falso.CriarClient(Base);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var relogio = System.Diagnostics.Stopwatch.StartNew();

        var act = () => http.GetAsync("v1/lento", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "o atraso de 30 s foi interrompido pelo token");
    }

    [Fact]
    public async Task Demora_RespondeDepoisDoAtraso()
    {
        var falso = new HttpFalso();
        falso.Quando(HttpMethod.Get, "/v1/lento").Demora(TimeSpan.FromMilliseconds(20), HttpStatusCode.OK, """{"ok":true}""");
        using var http = falso.CriarClient(Base);

        var corpo = await http.GetStringAsync("v1/lento");

        corpo.Should().Be("""{"ok":true}""");
    }

    [Fact]
    public void CaminhoComQuery_ERecusadoNoRegistro()
    {
        var falso = new HttpFalso();

        var act = () => falso.Quando(HttpMethod.Get, "/v1/busca?q=bolo");

        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("predicado");
    }

    [Fact]
    public void CriarClient_UsaOBaseAddressInformado()
    {
        var falso = new HttpFalso();

        using var http = falso.CriarClient("https://api.mp.test/");

        http.BaseAddress.Should().Be(new Uri("https://api.mp.test/"));
    }
}
