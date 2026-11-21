using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Pdf;
using EasyStock.Infra.Async.Pdf;
using Microsoft.Extensions.Configuration;

namespace EasyStock.Infra.Async.UnitTests.Pdf;

/// <summary>#3017 (E3): o impresso HTML vira PNG pelo Gotenberg; qualquer falha vira exceção para a conversa cair no texto.</summary>
public sealed class GotenbergRenderizadorHtmlTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private sealed class Manipulador(Func<HttpRequestMessage, Task<HttpResponseMessage>> resposta) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string Corpo { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uri = request.RequestUri;
            Corpo = await request.Content!.ReadAsStringAsync(ct);
            return await resposta(request);
        }
    }

    private static GotenbergRenderizadorHtml Criar(Manipulador m, string? url = "http://render:3000/")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [GotenbergRenderizadorHtml.ChaveUrl] = url }).Build();
        return new GotenbergRenderizadorHtml(new HttpClient(m), config);
    }

    private static DocumentoHtml Documento() => new("<html><body class=\"im\">Pix</body></html>",
        [new RecursoHtml("Lora.ttf", "font/ttf", Encoding.ASCII.GetBytes("FONTE-LORA")),
         new RecursoHtml("NunitoSans.ttf", "font/ttf", Encoding.ASCII.GetBytes("FONTE-NUNITO"))]);

    private static Task<HttpResponseMessage> Ok() =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) });

    [Fact]
    public async Task EnviaHtmlFontesEParametrosDoRecorte()
    {
        var m = new Manipulador(_ => Ok());

        var anexo = await Criar(m).RenderizarPngAsync(Documento(), 80);

        Assert.Equal("http://render:3000/forms/chromium/screenshot/html", m.Uri!.ToString());
        Assert.Equal("image/png", anexo.ContentType);
        Assert.Equal(Png, anexo.Conteudo);
        Assert.Contains("filename=index.html", m.Corpo);
        Assert.Contains("filename=Lora.ttf", m.Corpo);
        Assert.Contains("filename=NunitoSans.ttf", m.Corpo);
        Assert.Contains("FONTE-LORA", m.Corpo);
        Assert.Contains("name=selector\r\n\r\n.im", m.Corpo);
        Assert.Contains("name=deviceScaleFactor\r\n\r\n3", m.Corpo);
        Assert.Contains("name=failOnResourceLoadingFailed\r\n\r\ntrue", m.Corpo);
        Assert.Contains("name=waitForExpression", m.Corpo);
        Assert.Contains("name=width\r\n\r\n302", m.Corpo);
    }

    [Fact]
    public async Task ErroHttp_VirouExcecao_SemOCorpoDaResposta()
    {
        var m = new Manipulador(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        { Content = new StringContent("CODIGO-PIX-SECRETO") }));

        var erro = await Assert.ThrowsAsync<HttpRequestException>(() => Criar(m).RenderizarPngAsync(Documento(), 80));

        Assert.DoesNotContain("CODIGO-PIX-SECRETO", erro.Message);
    }

    [Fact]
    public async Task Timeout_VirouTimeoutException()
    {
        var m = new Manipulador(_ => throw new TaskCanceledException("timeout"));

        await Assert.ThrowsAsync<TimeoutException>(() => Criar(m).RenderizarPngAsync(Documento(), 80));
    }

    [Fact]
    public async Task RespostaQueNaoEPng_VirouExcecao()
    {
        var m = new Manipulador(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") }));

        await Assert.ThrowsAsync<InvalidDataException>(() => Criar(m).RenderizarPngAsync(Documento(), 80));
    }

    [Fact]
    public async Task SemUrl_VirouExcecao()
    {
        var m = new Manipulador(_ => Ok());

        await Assert.ThrowsAsync<InvalidOperationException>(() => Criar(m, url: null).RenderizarPngAsync(Documento(), 80));
    }
}
