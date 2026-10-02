using System.Net;
using System.Text;
using EasyStock.Infra.Integrations.Ia;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.UnitTests.Ia;

/// <summary>#1398: HTTP stubado, sem rede e sem chave real.</summary>
public class FireworksTranscritorAudioTests
{
    private static FireworksTranscritorAudio Criar(CapturaHandler handler, TranscricaoAudioOptions? opcoes = null)
    {
        opcoes ??= new TranscricaoAudioOptions { ApiKey = "chave-de-teste" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://audio.test/") };
        return new FireworksTranscritorAudio(http, Options.Create(opcoes), NullLogger<FireworksTranscritorAudio>.Instance);
    }

    [Fact]
    public async Task EnviaMultipartERetornaTexto()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, """{"text":" quero um bolo de cenoura "}""");

        var texto = await Criar(handler).TranscreverAsync([1, 2, 3], "audio/ogg; codecs=opus");

        texto.Should().Be("quero um bolo de cenoura");
        handler.Requisicao!.RequestUri!.AbsoluteUri.Should().Be("https://audio.test/v1/audio/transcriptions");
        handler.Requisicao.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requisicao.Headers.Authorization.Parameter.Should().Be("chave-de-teste");
        handler.Corpo.Should().Contain("name=file").And.Contain("filename=audio.ogg")
            .And.Contain("whisper-v3-turbo").And.Contain("name=language");
    }

    [Fact]
    public async Task ErroHttpLanca()
    {
        var handler = new CapturaHandler(HttpStatusCode.ServiceUnavailable, """{"error":"x"}""");

        var acao = () => Criar(handler).TranscreverAsync([1], "audio/ogg");

        await acao.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void SemChaveFicaIndisponivel()
    {
        Criar(new CapturaHandler(HttpStatusCode.OK, "{}"), new TranscricaoAudioOptions()).Disponivel.Should().BeFalse();
        Criar(new CapturaHandler(HttpStatusCode.OK, "{}"), new TranscricaoAudioOptions { ApiKey = "k", Enabled = false })
            .Disponivel.Should().BeFalse();
    }

    private sealed class CapturaHandler(HttpStatusCode status, string resposta) : HttpMessageHandler
    {
        public HttpRequestMessage? Requisicao { get; private set; }
        public string? Corpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requisicao = request;
            Corpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(resposta, Encoding.UTF8, "application/json") };
        }
    }
}
