using System.Net;
using System.Text;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Infra.Integrations.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>#1417: Graph do Embedded Signup com HTTP stubado. Prova URL, verbo, corpo e que nada sensível vai ao log.</summary>
public class MetaEmbeddedSignupClientTests
{
    private const string Token = "EAAG-token-da-empresa";
    private const string Segredo = "segredo-do-app";
    private const string Code = "code-de-uso-unico";

    private static MetaEmbeddedSignupClient Criar(CapturaHandler handler, LoggerDeCaptura? logger = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.test/v26.0/") },
            Options.Create(new WhatsAppCloudOptions { AppId = "123", AppSecret = Segredo }),
            logger ?? new LoggerDeCaptura());

    [Fact]
    public async Task TrocaOCodePeloTokenComOSegredoDoApp()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, """{"access_token":"EAAG-novo","token_type":"bearer"}""");

        var token = await Criar(handler).TrocarCodigoPorTokenAsync(Code);

        token.Should().Be("EAAG-novo");
        handler.Requisicao!.Method.Should().Be(HttpMethod.Get);
        handler.Requisicao.RequestUri!.AbsoluteUri.Should().Be(
            $"https://graph.test/v26.0/oauth/access_token?client_id=123&client_secret={Segredo}&code={Code}");
        handler.Requisicao.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task CodeInvalidoLancaSemVazarCodeNemSegredoNoLog()
    {
        var handler = new CapturaHandler(HttpStatusCode.BadRequest,
            """{"error":{"message":"Invalid verification code format.","type":"OAuthException","code":100}}""");
        var logger = new LoggerDeCaptura();

        var acao = () => Criar(handler, logger).TrocarCodigoPorTokenAsync(Code);

        var erro = await acao.Should().ThrowAsync<WhatsAppCloudException>();
        erro.Which.Codigo.Should().Be(100);
        erro.Which.StatusHttp.Should().Be(400);
        logger.Linhas.Should().NotBeEmpty();
        logger.Linhas.Should().NotContain(l => l.Contains(Code) || l.Contains(Segredo));
    }

    [Fact]
    public async Task InscreveOAppNaWabaComOTokenDaEmpresa()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, """{"success":true}""");

        await Criar(handler).InscreverAppNaWabaAsync("9988", Token);

        handler.Requisicao!.Method.Should().Be(HttpMethod.Post);
        handler.Requisicao.RequestUri!.AbsoluteUri.Should().Be("https://graph.test/v26.0/9988/subscribed_apps");
        handler.Requisicao.Headers.Authorization!.Parameter.Should().Be(Token);
    }

    [Fact]
    public async Task ConsultaONumeroComOsCamposDaCoexistencia()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK,
            """{"display_phone_number":"+55 11 92703-2814","verified_name":"Casa da Baba","is_on_biz_app":true,"platform_type":"CLOUD_API","id":"555"}""");

        var numero = await Criar(handler).ConsultarNumeroAsync("555", Token);

        numero.Should().Be(new NumeroWhatsAppMeta("+55 11 92703-2814", "Casa da Baba", true, "CLOUD_API"));
        handler.Requisicao!.RequestUri!.AbsoluteUri.Should().Be(
            "https://graph.test/v26.0/555?fields=display_phone_number,verified_name,is_on_biz_app,platform_type");
        handler.Requisicao.Headers.Authorization!.Parameter.Should().Be(Token);
    }

    [Theory]
    [InlineData(TipoSincronizacaoWhatsApp.EstadoDoApp, "smb_app_state_sync")]
    [InlineData(TipoSincronizacaoWhatsApp.Historico, "history")]
    public async Task PedeASincronizacaoEDevolveORequestId(TipoSincronizacaoWhatsApp tipo, string syncType)
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, """{"messaging_product":"whatsapp","request_id":"req-1"}""");

        var requestId = await Criar(handler).SolicitarSincronizacaoAsync("555", Token, tipo);

        requestId.Should().Be("req-1");
        handler.Requisicao!.Method.Should().Be(HttpMethod.Post);
        handler.Requisicao.RequestUri!.AbsoluteUri.Should().Be("https://graph.test/v26.0/555/smb_app_data");
        handler.Corpo.Should().Be($$"""{"messaging_product":"whatsapp","sync_type":"{{syncType}}"}""");
    }

    [Fact]
    public async Task FalhaComTokenNaoLogaOToken()
    {
        var handler = new CapturaHandler(HttpStatusCode.Unauthorized,
            """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}""");
        var logger = new LoggerDeCaptura();

        var acao = () => Criar(handler, logger).SolicitarSincronizacaoAsync("555", Token, TipoSincronizacaoWhatsApp.Historico);

        (await acao.Should().ThrowAsync<WhatsAppCloudException>()).Which.Codigo.Should().Be(190);
        logger.Linhas.Should().NotContain(l => l.Contains(Token));
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

    private sealed class LoggerDeCaptura : ILogger<MetaEmbeddedSignupClient>
    {
        public List<string> Linhas { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Linhas.Add(formatter(state, exception) + (exception?.ToString() ?? ""));
    }
}
