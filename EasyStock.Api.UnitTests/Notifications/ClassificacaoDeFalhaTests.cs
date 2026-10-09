using System.Net;
using System.Security.Cryptography;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Options;
using EasyStock.Infra.Notifications.Push;
using EasyStock.Infra.Notifications.Sms;
using EasyStock.Infra.Notifications.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebPush;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// N2: a falha é classificada pelo protocolo, e quem repete é só o outbox. HTTP 4xx (menos 408 e 429) e SMTP 5xx
/// são permanentes; HTTP 5xx, 408, 429, rede e SMTP 4xx são transitórios. WhatsApp e SMS saem no máximo uma vez:
/// timeout, queda de conexão e 5xx deixam a mensagem <see cref="DesfechoEnvio.Indeterminado"/>. Cada teste confere
/// também o número de chamadas ao provider (a retentativa aninhada fazia até 12 por rodada).
/// </summary>
public class ClassificacaoDeFalhaTests
{
    private static readonly Guid EmpresaPush = Guid.NewGuid();
    private const string Telefone = "+5511999990001";
    private const string Email = "maria.souza@example.com";

    private static MensagemPronta Mensagem(CanalNotificacao canal, string destinatario) =>
        new(Guid.NewGuid(), Guid.NewGuid(), destinatario, "Assunto", "Corpo", canal, CategoriaConteudoNotificacao.Transacional);

    // ----- SMTP (e-mail) -----
    // N3: a classificacao do SMTP saiu do canal e foi para o servico sobre MailKit. Os casos daqui (550 permanente,
    // 421 transitorio e uma chamada so, tabela de codigos 4xx e 5xx, falha de rede) agora sao exercitados no protocolo
    // de verdade: EasyStock.Infra.Async.UnitTests/Email/ClassificadorFalhaSmtpTests (tabela de codigos e rede) e
    // SmtpEmailServiceTests (servidor SMTP falso em loopback, uma conexao por envio).

    // ----- Twilio (SMS e WhatsApp saem no máximo uma vez) -----

    /// <summary>Entrega um HttpClient novo por chamada e conta quantas vezes a API (o handler) foi chamada.</summary>
    private sealed class FabricaHttpContada : IHttpClientFactory
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _responder;

        public int Chamadas;

        public FabricaHttpContada(HttpStatusCode status)
            : this(_ => Task.FromResult(new HttpResponseMessage(status)))
        {
        }

        public FabricaHttpContada(Func<CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

        public HttpClient CreateClient(string name) => new(new Handler(this));

        private sealed class Handler(FabricaHttpContada fabrica) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref fabrica.Chamadas);
                return fabrica._responder(cancellationToken);
            }
        }
    }

    private static TwilioSmsProvider TwilioSms(FabricaHttpContada http) =>
        new(http, Options.Create(new TwilioSmsOptions()), NullLogger<TwilioSmsProvider>.Instance);

    private static TwilioWhatsAppProvider TwilioWhatsApp(FabricaHttpContada http) =>
        new(http, Options.Create(new TwilioWhatsAppOptions()), NullLogger<TwilioWhatsAppProvider>.Instance);

    [Fact]
    public async Task Twilio_sms_400_e_permanente_e_chama_a_API_uma_vez()
    {
        var http = new FabricaHttpContada(HttpStatusCode.BadRequest);

        var resultado = await TwilioSms(http).EnviarAsync(Mensagem(CanalNotificacao.Sms, Telefone));

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        resultado.FalhaPermanente.Should().BeTrue();
        resultado.StatusHttp.Should().Be(400);
        resultado.ProviderUsado.Should().Be("twilio");
        http.Chamadas.Should().Be(1, "o 4xx é recusa do Twilio: repetir só gasta chamada");
    }

    [Fact]
    public async Task Twilio_whatsapp_503_e_indeterminado()
    {
        var http = new FabricaHttpContada(HttpStatusCode.ServiceUnavailable);

        var resultado = await TwilioWhatsApp(http).EnviarAsync(Mensagem(CanalNotificacao.WhatsApp, Telefone));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Indeterminado);
        resultado.Sucesso.Should().BeFalse();
        resultado.FalhaPermanente.Should().BeFalse();
        resultado.StatusHttp.Should().Be(503);
        http.Chamadas.Should().Be(1, "num 5xx a mensagem pode já ter saído: repetir duplicaria");
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, DesfechoEnvio.Enviado)]
    [InlineData(HttpStatusCode.BadRequest, DesfechoEnvio.FalhaPermanente)]
    [InlineData(HttpStatusCode.Unauthorized, DesfechoEnvio.FalhaPermanente)]
    [InlineData(HttpStatusCode.NotFound, DesfechoEnvio.FalhaPermanente)]
    [InlineData(HttpStatusCode.RequestTimeout, DesfechoEnvio.FalhaTransitoria)]
    [InlineData(HttpStatusCode.TooManyRequests, DesfechoEnvio.FalhaTransitoria)]
    [InlineData(HttpStatusCode.InternalServerError, DesfechoEnvio.Indeterminado)]
    [InlineData(HttpStatusCode.BadGateway, DesfechoEnvio.Indeterminado)]
    public async Task Twilio_classifica_pelo_status_http_sem_retentativa(HttpStatusCode status, DesfechoEnvio esperado)
    {
        var sms = new FabricaHttpContada(status);
        var whatsapp = new FabricaHttpContada(status);

        var resultadoSms = await TwilioSms(sms).EnviarAsync(Mensagem(CanalNotificacao.Sms, Telefone));
        var resultadoWhatsApp = await TwilioWhatsApp(whatsapp).EnviarAsync(Mensagem(CanalNotificacao.WhatsApp, Telefone));

        resultadoSms.Desfecho.Should().Be(esperado);
        resultadoWhatsApp.Desfecho.Should().Be(esperado);
        resultadoSms.StatusHttp.Should().Be((int)status);
        sms.Chamadas.Should().Be(1);
        whatsapp.Chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Twilio_timeout_e_queda_de_conexao_viram_Indeterminado()
    {
        var timeout = new FabricaHttpContada(_ => throw new TaskCanceledException("timeout", new TimeoutException()));
        var queda = new FabricaHttpContada(_ => throw new HttpRequestException("conexão encerrada"));

        var porTimeout = await TwilioSms(timeout).EnviarAsync(Mensagem(CanalNotificacao.Sms, Telefone));
        var porQueda = await TwilioWhatsApp(queda).EnviarAsync(Mensagem(CanalNotificacao.WhatsApp, Telefone));

        porTimeout.Desfecho.Should().Be(DesfechoEnvio.Indeterminado);
        porQueda.Desfecho.Should().Be(DesfechoEnvio.Indeterminado);
        timeout.Chamadas.Should().Be(1);
        queda.Chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Twilio_com_cancelamento_do_chamador_propaga_em_vez_de_virar_falha()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var http = new FabricaHttpContada(ct => Task.FromCanceled<HttpResponseMessage>(ct));

        var act = () => TwilioSms(http).EnviarAsync(Mensagem(CanalNotificacao.Sms, Telefone), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ----- Web Push -----

    private static WebPushOptions OpcoesVapid()
    {
        var chaves = VapidHelper.GenerateVapidKeys();
        return new WebPushOptions { Subject = "mailto:teste@example.com", PublicKey = chaves.PublicKey, PrivateKey = chaves.PrivateKey };
    }

    private static WebPushSubscription Inscricao(Guid usuarioId, string endpoint = "https://push.example.test/send/abc")
    {
        // Chave pública P-256 (65 bytes, formato não comprimido) e segredo de 16 bytes: o que o navegador envia.
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = ecdh.ExportParameters(false).Q;
        var p256dh = new byte[65];
        p256dh[0] = 0x04;
        q.X!.CopyTo(p256dh, 1);
        q.Y!.CopyTo(p256dh, 33);
        return WebPushSubscription.Criar(endpoint,
            System.Buffers.Text.Base64Url.EncodeToString(p256dh),
            System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
            empresaId: EmpresaPush, usuarioId: usuarioId);
    }

    private static MensagemPronta MensagemPush(Guid usuarioId) =>
        Mensagem(CanalNotificacao.Push, $"usuario:{usuarioId}") with { EmpresaId = EmpresaPush };

    [Fact]
    public async Task WebPush_NaoEntregaEmOutraEmpresaOuOutroDestinatario()
    {
        var usuarioId = Guid.NewGuid();
        var outraEmpresa = Inscricao(usuarioId);
        outraEmpresa.EmpresaId = Guid.NewGuid();
        var outroUsuario = Inscricao(Guid.NewGuid());
        var (canal, _, handler) = CanalWebPush(usuarioId, _ => new HttpResponseMessage(HttpStatusCode.Created),
            outraEmpresa, outroUsuario);

        var resultado = await canal.EnviarAsync(MensagemPush(usuarioId));

        resultado.ErroDetalhado.Should().Be("NENHUMA_SUBSCRIPTION_ATIVA");
        handler.Chamadas.Should().Be(0);
    }

    private sealed class HandlerWebPush(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Chamadas;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Chamadas);
            return Task.FromResult(responder(request));
        }
    }

    private static (WebPushCanal Canal, IWebPushSubscriptionRepository Repo, HandlerWebPush Handler) CanalWebPush(
        Guid usuarioId, Func<HttpRequestMessage, HttpResponseMessage> responder, params WebPushSubscription[] inscricoes)
    {
        var repo = Substitute.For<IWebPushSubscriptionRepository>();
        repo.GetByUsuarioAsync(usuarioId, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<WebPushSubscription>)inscricoes);
        var handler = new HandlerWebPush(responder);
        var canal = new WebPushCanal(repo, Options.Create(OpcoesVapid()), NullLogger<WebPushCanal>.Instance,
            new WebPushClient(new HttpClient(handler)));
        return (canal, repo, handler);
    }

    [Fact]
    public void WebPush_resolve_no_container_sem_WebPushClient_registrado()
    {
        // O WebPushClient opcional do construtor é só um ponto de teste. O container do Worker não o registra, e o
        // valor padrão (nulo) precisa bastar, inclusive com ValidateOnBuild (o Worker sobe com ele ligado).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IWebPushSubscriptionRepository>());
        services.AddSingleton(Options.Create(OpcoesVapid()));
        services.AddScoped<ICanalNotificacao, WebPushCanal>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICanalNotificacao>().Should().BeOfType<WebPushCanal>();
    }

    [Fact]
    public async Task WebPush_sem_inscricao_ativa_e_permanente()
    {
        var usuarioId = Guid.NewGuid();
        var (canal, _, handler) = CanalWebPush(usuarioId, _ => new HttpResponseMessage(HttpStatusCode.Created));

        var resultado = await canal.EnviarAsync(MensagemPush(usuarioId));

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        resultado.ErroDetalhado.Should().Be("NENHUMA_SUBSCRIPTION_ATIVA");
        handler.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task WebPush_sem_chave_vapid_e_permanente()
    {
        var repo = Substitute.For<IWebPushSubscriptionRepository>();
        var canal = new WebPushCanal(repo, Options.Create(new WebPushOptions()), NullLogger<WebPushCanal>.Instance);

        var resultado = await canal.EnviarAsync(MensagemPush(Guid.NewGuid()));

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        await repo.DidNotReceiveWithAnyArgs().GetByUsuarioAsync(default, default);
    }

    [Fact]
    public async Task WebPush_400_e_permanente()
    {
        var usuarioId = Guid.NewGuid();
        var (canal, _, handler) = CanalWebPush(usuarioId, _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
            Inscricao(usuarioId));

        var resultado = await canal.EnviarAsync(MensagemPush(usuarioId));

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        resultado.Sucesso.Should().BeFalse();
        handler.Chamadas.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, true)]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, true)]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Gone, true)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task WebPush_classifica_pelo_status_http(HttpStatusCode status, bool permanente)
    {
        var usuarioId = Guid.NewGuid();
        var inscricao = Inscricao(usuarioId);
        var (canal, repo, _) = CanalWebPush(usuarioId, _ => new HttpResponseMessage(status), inscricao);

        var resultado = await canal.EnviarAsync(MensagemPush(usuarioId));

        resultado.FalhaPermanente.Should().Be(permanente);
        resultado.Desfecho.Should().Be(permanente ? DesfechoEnvio.FalhaPermanente : DesfechoEnvio.FalhaTransitoria);
        if (status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            await repo.Received(1).DesativarAsync(inscricao.Endpoint, Arg.Any<CancellationToken>());
        else
            await repo.DidNotReceiveWithAnyArgs().DesativarAsync(default!, default);
    }

    [Fact]
    public async Task WebPush_com_uma_inscricao_que_entrega_e_outra_que_falha_conta_como_enviado()
    {
        var usuarioId = Guid.NewGuid();
        var (canal, _, handler) = CanalWebPush(usuarioId,
            r => new HttpResponseMessage(r.RequestUri!.AbsolutePath.EndsWith("/boa", StringComparison.Ordinal)
                ? HttpStatusCode.Created
                : HttpStatusCode.BadRequest),
            Inscricao(usuarioId, "https://push.example.test/send/boa"),
            Inscricao(usuarioId, "https://push.example.test/send/ruim"));

        var resultado = await canal.EnviarAsync(MensagemPush(usuarioId));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        handler.Chamadas.Should().Be(2);
    }

    [Fact]
    public async Task WebPush_com_inscricoes_de_erros_mistos_e_transitorio()
    {
        // Uma recusa permanente e uma indisponibilidade: repetir ainda pode entregar na segunda.
        var usuarioId = Guid.NewGuid();
        var (canal, _, _) = CanalWebPush(usuarioId,
            r => new HttpResponseMessage(r.RequestUri!.AbsolutePath.EndsWith("/ruim", StringComparison.Ordinal)
                ? HttpStatusCode.BadRequest
                : HttpStatusCode.ServiceUnavailable),
            Inscricao(usuarioId, "https://push.example.test/send/ruim"),
            Inscricao(usuarioId, "https://push.example.test/send/fora"));

        var resultado = await canal.EnviarAsync(MensagemPush(usuarioId));

        resultado.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
    }
}
