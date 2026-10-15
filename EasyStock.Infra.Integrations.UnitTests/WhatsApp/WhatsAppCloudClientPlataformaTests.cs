using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Integrations.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.UnitTests.WhatsApp;

/// <summary>
/// N6: o cliente de plataforma fala com a Meta pelo número fixo da plataforma, devolve o desfecho sem lançar e não
/// repete o POST. HTTP é stubado; nada sai pela rede.
/// </summary>
public class WhatsAppCloudClientPlataformaTests
{
    private const string NumeroPlataforma = "7770009999";
    private const string Telefone = "5511999998888";
    private const string RespostaOk = """{"messaging_product":"whatsapp","messages":[{"id":"wamid.PLAT"}]}""";

    private readonly IRemetenteWhatsApp _remetente = Substitute.For<IRemetenteWhatsApp>();

    private WhatsAppCloudClient Cliente(HandlerFake handler, ResiliencePipeline? pipeline = null, string numero = NumeroPlataforma)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.test/v26.0/") };
        var options = Options.Create(new WhatsAppCloudOptions
        {
            AccessToken = "token",
            PhoneNumberId = "1234567890",
            PhoneNumberIdPlataforma = numero
        });
        _remetente.ObterPhoneNumberIdAsync(Arg.Any<CancellationToken>()).Returns("5550001111");
        _remetente.HaTenantCorrente.Returns(true);
        var pipelines = Substitute.For<ResiliencePipelineProvider<string>>();
        pipelines.GetPipeline(Arg.Any<string>()).Returns(pipeline ?? ResiliencePipeline.Empty);
        return new WhatsAppCloudClient(http, options, _remetente, pipelines, NullLogger<WhatsAppCloudClient>.Instance);
    }

    private static EnvioTemplatePlataforma Envio(
        string[]? corpo = null, string? botao0 = null, bool copyCode = false) => new(
            Telefone, "codigo_redefinir_senha", "pt_BR", corpo ?? ["482913"], botao0, null, copyCode,
            "aaaaaaaabbbbccccddddeeeeeeeeeeee.11112222333344445555666677778888");

    private static JsonElement Corpo(HandlerFake h) => JsonDocument.Parse(h.UltimoCorpo!).RootElement;

    [Fact]
    public async Task TemplateDeAutenticacaoLevaOCodigoNoCorpoENoBotaoUrlIndiceZero()
    {
        var h = new HandlerFake().Responde(HttpStatusCode.OK, RespostaOk);

        var r = await Cliente(h).EnviarTemplatePlataformaAsync(Envio(["482913"], botao0: "482913", copyCode: true));

        r.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        r.Wamid.Should().Be("wamid.PLAT");
        var componentes = Corpo(h).GetProperty("template").GetProperty("components");
        var corpo = componentes.EnumerateArray().Single(c => c.GetProperty("type").GetString() == "body");
        corpo.GetProperty("parameters")[0].GetProperty("text").GetString().Should().Be("482913");
        var botao = componentes.EnumerateArray().Single(c => c.GetProperty("type").GetString() == "button");
        botao.GetProperty("sub_type").GetString().Should().Be("url");
        botao.GetProperty("index").GetString().Should().Be("0");
        botao.GetProperty("parameters")[0].GetProperty("type").GetString().Should().Be("text");
        botao.GetProperty("parameters")[0].GetProperty("text").GetString().Should().Be("482913");
    }

    [Fact]
    public async Task BotaoUrlDeConviteLevaSoOSufixo()
    {
        var h = new HandlerFake().Responde(HttpStatusCode.OK, RespostaOk);
        var sufixo = new string('a', 40); // o limite de 15 é do copy code, não do sufixo de URL

        var r = await Cliente(h).EnviarTemplatePlataformaAsync(Envio(["Maria"], botao0: sufixo));

        r.Desfecho.Should().Be(DesfechoEnvio.Enviado);
        var botao = Corpo(h).GetProperty("template").GetProperty("components").EnumerateArray()
            .Single(c => c.GetProperty("type").GetString() == "button");
        botao.GetProperty("parameters")[0].GetProperty("text").GetString().Should().Be(sufixo);
    }

    [Fact]
    public async Task CodigoComMaisDe15CaracteresNaoChamaARede()
    {
        var h = new HandlerFake().Responde(HttpStatusCode.OK, RespostaOk);
        var longo = new string('9', 16);

        var r = await Cliente(h).EnviarTemplatePlataformaAsync(Envio([longo], botao0: longo, copyCode: true));

        r.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        h.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task UsaOPhoneNumberIdDaPlataformaNuncaODoTenant()
    {
        var h = new HandlerFake().Responde(HttpStatusCode.OK, RespostaOk);
        var cliente = Cliente(h);

        await cliente.EnviarTemplatePlataformaAsync(Envio());
        await cliente.EnviarTextoPlataformaAsync(Telefone, "Este número só envia avisos.");

        h.Urls.Should().OnlyContain(u => u == $"https://graph.test/v26.0/{NumeroPlataforma}/messages");
        await _remetente.DidNotReceive().ObterPhoneNumberIdAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemPhoneNumberIdDaPlataformaFalhaPermanenteSemRede()
    {
        var h = new HandlerFake();

        var r = await Cliente(h, numero: "").EnviarTemplatePlataformaAsync(Envio());

        r.Desfecho.Should().Be(DesfechoEnvio.FalhaPermanente);
        h.Chamadas.Should().Be(0);
    }

    [Fact]
    public async Task EnviaOpacoComEmpresaEOutbox()
    {
        var h = new HandlerFake().Responde(HttpStatusCode.OK, RespostaOk);
        var envio = Envio();

        await Cliente(h).EnviarTemplatePlataformaAsync(envio);

        Corpo(h).GetProperty("biz_opaque_callback_data").GetString().Should().Be(envio.OpacoCallback);
    }

    [Fact]
    public async Task TimeoutDoPollyViraIndeterminadoComUmaSoChamada()
    {
        var h = new HandlerFake { Atraso = TimeSpan.FromSeconds(5) }.Responde(HttpStatusCode.OK, RespostaOk);
        var pipeline = new ResiliencePipelineBuilder().AddTimeout(TimeSpan.FromMilliseconds(100)).Build();

        var r = await Cliente(h, pipeline).EnviarTemplatePlataformaAsync(Envio());

        r.Desfecho.Should().Be(DesfechoEnvio.Indeterminado);
        h.Chamadas.Should().Be(1);
    }

    [Fact]
    public async Task QuedaDepoisDoEnvioViraIndeterminado()
    {
        var h = new HandlerFake().Falha(new HttpRequestException(HttpRequestError.ResponseEnded, "conexão caiu"));

        var r = await Cliente(h).EnviarTemplatePlataformaAsync(Envio());

        r.Desfecho.Should().Be(DesfechoEnvio.Indeterminado);
        h.Chamadas.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Http5xxViraIndeterminado(HttpStatusCode status)
    {
        var h = new HandlerFake().Responde(status, """{"error":{"message":"x","code":2}}""");

        var r = await Cliente(h).EnviarTemplatePlataformaAsync(Envio());

        r.Desfecho.Should().Be(DesfechoEnvio.Indeterminado);
        r.StatusHttp.Should().Be((int)status);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    public async Task FalhaDeConexaoAntesDoEnvioEhTransitoria(HttpRequestError erro)
    {
        var h = new HandlerFake().Falha(new HttpRequestException(erro, "nada saiu"));

        var r = await Cliente(h).EnviarTemplatePlataformaAsync(Envio());

        r.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
    }

    [Fact]
    public async Task DisjuntorAbertoEhTransitorio()
    {
        var controle = new CircuitBreakerManualControl();
        await controle.IsolateAsync();
        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions { ManualControl = controle }).Build();
        var h = new HandlerFake().Responde(HttpStatusCode.OK, RespostaOk);

        var r = await Cliente(h, pipeline).EnviarTemplatePlataformaAsync(Envio());

        r.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
        h.Chamadas.Should().Be(0);
    }

    [Theory]
    [InlineData(100, ClasseErroMeta.Permanente)]
    [InlineData(131026, ClasseErroMeta.Permanente)]
    [InlineData(132000, ClasseErroMeta.Permanente)]
    [InlineData(132001, ClasseErroMeta.Permanente)]
    [InlineData(131050, ClasseErroMeta.Permanente)]
    [InlineData(131042, ClasseErroMeta.PermanenteComAlerta)]
    [InlineData(190, ClasseErroMeta.PermanenteComAlerta)]
    [InlineData(131048, ClasseErroMeta.PermanenteComAlerta)]
    [InlineData(130429, ClasseErroMeta.Transitorio)]
    [InlineData(131056, ClasseErroMeta.Transitorio)]
    [InlineData(131000, ClasseErroMeta.Transitorio)]
    public async Task CodigosDaMetaSaoClassificadosPelaTabela(int codigo, ClasseErroMeta classe)
    {
        var h = new HandlerFake().Responde(HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"falha\",\"type\":\"OAuthException\",\"code\":" + codigo + "}}");

        var r = await Cliente(h).EnviarTemplatePlataformaAsync(Envio());

        r.Classe.Should().Be(classe);
        r.CodigoMeta.Should().Be(codigo);
        r.Desfecho.Should().Be(classe == ClasseErroMeta.Transitorio ? DesfechoEnvio.FalhaTransitoria : DesfechoEnvio.FalhaPermanente);
        h.Chamadas.Should().Be(1);
    }

    [Fact]
    public async Task CodigoForaDaTabelaEhTransitorio()
    {
        var h = new HandlerFake().Responde(HttpStatusCode.BadRequest, """{"error":{"message":"?","code":999999}}""");

        var r = await Cliente(h).EnviarTemplatePlataformaAsync(Envio());

        r.Classe.Should().Be(ClasseErroMeta.Transitorio);
        r.Desfecho.Should().Be(DesfechoEnvio.FalhaTransitoria);
    }

    private sealed class HandlerFake : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body, Exception? Falha)> _fila = new();

        public int Chamadas { get; private set; }
        public string? UltimoCorpo { get; private set; }
        public List<string> Urls { get; } = [];
        public TimeSpan Atraso { get; init; }

        public HandlerFake Responde(HttpStatusCode status, string body)
        {
            _fila.Enqueue((status, body, null));
            return this;
        }

        public HandlerFake Falha(Exception falha)
        {
            _fila.Enqueue((default, "", falha));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Chamadas++;
            Urls.Add(request.RequestUri!.ToString());
            if (request.Content is not null) UltimoCorpo = await request.Content.ReadAsStringAsync(ct);
            if (Atraso > TimeSpan.Zero) await Task.Delay(Atraso, ct);
            var (status, body, falha) = _fila.Count > 0 ? _fila.Dequeue() : (HttpStatusCode.OK, "{}", null);
            if (falha is not null) throw falha;
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
