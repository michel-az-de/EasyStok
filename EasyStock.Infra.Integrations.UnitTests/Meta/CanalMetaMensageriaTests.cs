using System.Net;
using System.Text;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Integrations.Meta;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Polly;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.UnitTests.Meta;

/// <summary>
/// Messenger e Instagram (S35) com HTTP stubado: dentro da janela sai <c>RESPONSE</c>, fora dela a
/// tag <c>HUMAN_AGENT</c>; o envio vai a <c>me/messages</c> e devolve o <c>message_id</c>; erro da
/// Meta vira <see cref="MetaMensageriaException"/> com código e subcódigo.
/// </summary>
public class CanalMetaMensageriaTests
{
    private readonly Handler _handler = new();
    private readonly MetaMensageriaHttpTransporte _transporte;

    public CanalMetaMensageriaTests()
    {
        var http = new HttpClient(_handler) { BaseAddress = new Uri("https://graph.test/v25.0/") };
        var pipelines = Substitute.For<ResiliencePipelineProvider<string>>();
        pipelines.GetPipeline(Arg.Any<string>()).Returns(ResiliencePipeline.Empty);
        _transporte = new MetaMensageriaHttpTransporte(http, pipelines, NullLogger<MetaMensageriaHttpTransporte>.Instance);
    }

    [Fact]
    public async Task DentroDaJanela_Response()
    {
        _handler.Responder(HttpStatusCode.OK, """{"recipient_id":"PSID-3","message_id":"m_ok"}""");

        var id = await new CanalMessenger(_transporte).EnviarTextoAsync("PSID-3", "Oi!");

        id.Should().Be("m_ok");
        _handler.UltimaUri!.AbsoluteUri.Should().Be("https://graph.test/v25.0/me/messages");
        _handler.UltimoCorpo.Should().Contain("\"recipient\":{\"id\":\"PSID-3\"}")
            .And.Contain("\"messaging_type\":\"RESPONSE\"")
            .And.Contain("\"text\":\"Oi!\"")
            .And.NotContain("\"tag\"");
    }

    [Fact]
    public async Task ForaDaJanelaUsaHumanAgent()
    {
        _handler.Responder(HttpStatusCode.OK, """{"message_id":"m_tag"}""");

        await new CanalInstagram(_transporte).EnviarTextoComTagAsync("IGSID-9", "Voltei!", CapacidadesCanal.TagAgenteHumano);

        _handler.UltimoCorpo.Should().Contain("\"messaging_type\":\"MESSAGE_TAG\"").And.Contain("\"tag\":\"HUMAN_AGENT\"");
    }

    [Fact]
    public async Task ErroDaMeta_ViraExcecaoComCodigo()
    {
        _handler.Responder(HttpStatusCode.BadRequest,
            """{"error":{"message":"(#10) Esta mensagem foi enviada fora da janela permitida.","code":10,"error_subcode":2018278}}""");

        var act = () => new CanalMessenger(_transporte).EnviarTextoAsync("PSID-3", "oi");

        var erro = (await act.Should().ThrowAsync<MetaMensageriaException>()).Which;
        erro.Codigo.Should().Be(10);
        erro.Subcodigo.Should().Be(2018278);
    }

    [Fact]
    public async Task ImagemComLegenda_SaiTextoDepoisAnexo()
    {
        var stub = new StubMetaMensageriaTransporte();

        await new CanalInstagram(stub).EnviarImagemAsync("IGSID-9", "https://cdn/x.jpg", "Nosso bolo");

        stub.Enviados.Should().HaveCount(2);
        stub.Enviados.First().Should().Contain("Nosso bolo");
        stub.Enviados.Last().Should().Contain("\"type\":\"image\"").And.Contain("https://cdn/x.jpg");
    }

    [Fact]
    public async Task BotoesSoNoMessenger()
    {
        var stub = new StubMetaMensageriaTransporte();
        (string, string)[] botoes = [("acao:cardapio", "Cardápio"), ("acao:humano", "Falar com a loja")];

        await new CanalMessenger(stub).EnviarBotoesAsync("PSID-3", "Como posso ajudar?", botoes);
        var instagram = () => new CanalInstagram(stub).EnviarBotoesAsync("IGSID-9", "x", botoes);

        stub.Enviados.Single().Should().Contain("\"quick_replies\"").And.Contain("\"payload\":\"acao:cardapio\"");
        await instagram.Should().ThrowAsync<NotSupportedException>();
        CapacidadesCanal.Para(CanalConversa.Instagram).AceitaBotoes.Should().BeFalse();
    }

    [Fact]
    public async Task SemModelo()
    {
        var act = () => new CanalMessenger(new StubMetaMensageriaTransporte()).EnviarModeloAsync("PSID-3", "x", "pt_BR", []);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    private sealed class Handler : HttpMessageHandler
    {
        private (HttpStatusCode Status, string Corpo) _resposta = (HttpStatusCode.OK, "{}");

        public Uri? UltimaUri { get; private set; }
        public string? UltimoCorpo { get; private set; }

        public void Responder(HttpStatusCode status, string corpo) => _resposta = (status, corpo);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            UltimaUri = request.RequestUri;
            UltimoCorpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_resposta.Status) { Content = new StringContent(_resposta.Corpo, Encoding.UTF8, "application/json") };
        }
    }
}
