using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Infra.Integrations.Ia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace EasyStock.Infra.Integrations.UnitTests.Ia;

/// <summary>
/// #3064 (S2b de #2948): o provedor da loja com as travas da spec §6. HTTP falso no formato que a DeepSeek documenta e a S0
/// observou (bloco <c>thinking</c> antes do <c>tool_use</c>, id <c>call_00_...</c>); nenhuma chamada real, nenhuma chave real.
/// </summary>
public class ClienteProvedorIaTests
{
    private const string Chave = "valor-de-teste-do-provedor";
    private const string BaseDeepSeek = "https://api.deepseek.com/anthropic";

    private const string RespostaToolUseDeepSeek = """
        {"id":"msg_ds","type":"message","role":"assistant","model":"deepseek-flash",
         "content":[
           {"type":"thinking","thinking":"Preciso consultar o pedido.","signature":"sig-ds-1"},
           {"type":"tool_use","id":"call_00_abc123","name":"consultar_pedido","input":{"pedido_id":"p-1"}}],
         "stop_reason":"tool_use","usage":{"input_tokens":120,"output_tokens":30,"cache_read_input_tokens":40,"cache_creation_input_tokens":0}}
        """;

    private const string RespostaTexto = """
        {"model":"deepseek-flash","content":[{"type":"text","text":"Seu pedido sai às 12h."}],
         "stop_reason":"end_turn","usage":{"input_tokens":200,"output_tokens":10}}
        """;

    private readonly LogEmLista _log = new();

    private ClienteProvedorIa Criar(FilaHandler handler, ResiliencePipelineRegistry<string>? registro = null) =>
        new(new Fabrica(handler), Options.Create(new AnthropicAgenteOptions { MaxTokensAgente = 1024, TimeoutSegundos = 30 }),
            registro ?? new ResiliencePipelineRegistry<string>(), _log);

    private static DestinoIa Destino(AutenticacaoProvedorIa auth = AutenticacaoProvedorIa.XApiKey, string baseUrl = BaseDeepSeek,
        Guid? provedor = null) => new(provedor ?? Guid.NewGuid(), baseUrl, auth, Chave, "deepseek-flash");

    private static RequisicaoLlm Requisicao(params MensagemLlm[] mensagens) =>
        new("sistema", mensagens.Length > 0 ? mensagens : [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("cadê meu pedido?")])],
            [new FerramentaLlm("consultar_pedido", "Consulta", """{"type":"object"}""")], "claude-sonnet-5");

    [Fact]
    public async Task XApiKeyVaiNoHeaderComURLAbsolutaEModeloDoDestino()
    {
        var handler = new FilaHandler((HttpStatusCode.OK, RespostaTexto));

        var resposta = await Criar(handler).EnviarAsync(Destino(), Requisicao());

        var req = handler.Requisicoes.Single();
        req.Uri.Should().Be("https://api.deepseek.com/anthropic/v1/messages");
        req.Headers["x-api-key"].Should().Be(Chave);
        req.Headers.Should().NotContainKey("Authorization");
        req.Headers["anthropic-version"].Should().Be("2023-06-01");
        using var corpo = JsonDocument.Parse(req.Corpo);
        corpo.RootElement.GetProperty("model").GetString().Should().Be("deepseek-flash");
        corpo.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(1024);
        resposta.Modelo.Should().Be("deepseek-flash");
        (resposta.TokensEntrada, resposta.TokensSaida).Should().Be((200, 10));
    }

    [Fact]
    public async Task BearerVaiNoAuthorizationSemXApiKey()
    {
        var handler = new FilaHandler((HttpStatusCode.OK, RespostaTexto));

        await Criar(handler).EnviarAsync(Destino(AutenticacaoProvedorIa.Bearer), Requisicao());

        var req = handler.Requisicoes.Single();
        req.Headers["Authorization"].Should().Be($"Bearer {Chave}");
        req.Headers.Should().NotContainKey("x-api-key");
    }

    [Fact]
    public async Task SegundoTurnoComToolResultDevolveOThinkingIntactoAntesDoToolUse()
    {
        var handler = new FilaHandler((HttpStatusCode.OK, RespostaToolUseDeepSeek), (HttpStatusCode.OK, RespostaTexto));
        var cliente = Criar(handler);
        var destino = Destino();
        var pergunta = new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("cadê meu pedido?")]);

        var primeira = await cliente.EnviarAsync(destino, Requisicao(pergunta));
        primeira.StopReason.Should().Be(RespostaLlm.StopToolUse);
        primeira.TokensCacheLido.Should().Be(40);
        var uso = primeira.Conteudo.OfType<BlocoUsoFerramentaLlm>().Single();
        uso.Id.Should().Be("call_00_abc123");

        await cliente.EnviarAsync(destino, Requisicao(pergunta,
            new MensagemLlm(MensagemLlm.Assistente, primeira.Conteudo),
            new MensagemLlm(MensagemLlm.Usuario, [new BlocoResultadoFerramentaLlm(uso.Id, """{"status":"em preparo"}""")])));

        using var corpo = JsonDocument.Parse(handler.Requisicoes[1].Corpo);
        var mensagens = corpo.RootElement.GetProperty("messages");
        var assistente = mensagens[1].GetProperty("content");
        assistente[0].GetProperty("type").GetString().Should().Be("thinking");
        assistente[0].GetProperty("thinking").GetString().Should().Be("Preciso consultar o pedido.");
        assistente[0].GetProperty("signature").GetString().Should().Be("sig-ds-1");
        assistente[1].GetProperty("type").GetString().Should().Be("tool_use");
        assistente[1].GetProperty("id").GetString().Should().Be("call_00_abc123");
        var resultado = mensagens[2].GetProperty("content")[0];
        resultado.GetProperty("type").GetString().Should().Be("tool_result");
        resultado.GetProperty("tool_use_id").GetString().Should().Be("call_00_abc123");
    }

    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ErroHttpLancaComOStatusESemAChaveNoLog(HttpStatusCode status)
    {
        var handler = new FilaHandler((status, "{\"error\":{\"message\":\"chave " + Chave + " sem saldo\"}}"));

        var ex = await FluentActions.Awaiting(() => Criar(handler).EnviarAsync(Destino(), Requisicao()))
            .Should().ThrowAsync<HttpRequestException>();

        ex.Which.StatusCode.Should().Be(status);
        ex.Which.Should().NotBeOfType<ProvedorIaIndisponivelException>();
        ex.Which.ToString().Should().NotContain(Chave);
        _log.Linhas.Should().Contain(l => l.Contains("***")).And.NotContain(l => l.Contains(Chave));
    }

    [Fact]
    public async Task ChaveNoLimiteDoCorteDoLogNaoVazaPelaMetade()
    {
        var handler = new FilaHandler((HttpStatusCode.BadRequest, new string('x', 490) + Chave));

        await FluentActions.Awaiting(() => Criar(handler).EnviarAsync(Destino(), Requisicao()))
            .Should().ThrowAsync<HttpRequestException>();

        var comeco = Chave.Substring(0, 10);
        _log.Linhas.Should().NotContain(l => l.Contains(comeco));
    }

    [Fact]
    public async Task O429NaoAbreODisjuntorMasO503Abre()
    {
        var registro = new ResiliencePipelineRegistry<string>();
        var provedor = Guid.NewGuid();
        var limite = new FilaHandler(Enumerable.Repeat((HttpStatusCode.TooManyRequests, "{}"), 20).ToArray());
        var cliente429 = Criar(limite, registro);
        for (var i = 0; i < 20; i++)
            await FluentActions.Awaiting(() => cliente429.EnviarAsync(Destino(provedor: provedor), Requisicao()))
                .Should().ThrowAsync<HttpRequestException>().Where(e => e.StatusCode == HttpStatusCode.TooManyRequests);
        limite.Requisicoes.Should().HaveCount(20, "o 429 não abre o disjuntor: toda chamada sai");

        var outro = Guid.NewGuid();
        var queda = new FilaHandler(Enumerable.Repeat((HttpStatusCode.ServiceUnavailable, "{}"), 20).ToArray());
        var cliente503 = Criar(queda, registro);
        for (var i = 0; i < 8; i++)
            await FluentActions.Awaiting(() => cliente503.EnviarAsync(Destino(provedor: outro), Requisicao()))
                .Should().ThrowAsync<HttpRequestException>();
        await FluentActions.Awaiting(() => cliente503.EnviarAsync(Destino(provedor: outro), Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>("disjuntor aberto: a chamada nem sai");
        queda.Requisicoes.Should().HaveCount(8);

        // Disjuntor por provedor: o aberto não derruba o vizinho.
        var ok = new FilaHandler((HttpStatusCode.OK, RespostaTexto));
        await Criar(ok, registro).EnviarAsync(Destino(provedor: provedor), Requisicao());
        ok.Requisicoes.Should().ContainSingle();
    }

    [Theory]
    [InlineData("http://api.deepseek.com/anthropic")]
    [InlineData("https://api.deepseek.com:8443/anthropic")]
    [InlineData("https://usuario@api.deepseek.com/anthropic")]
    [InlineData("https://evil.example.com/anthropic")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://localhost/anthropic")]
    [InlineData("https://api.deepseek.com.evil.com/anthropic")]
    [InlineData("https://api.deepseek.com/anthropic?x=1")]
    public async Task URLForaDasTravasNaoSai(string url)
    {
        var handler = new FilaHandler((HttpStatusCode.OK, RespostaTexto));

        await FluentActions.Awaiting(() => Criar(handler).EnviarAsync(Destino(baseUrl: url), Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();
        handler.Requisicoes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://api.anthropic.com", "https://api.anthropic.com/v1/messages")]
    [InlineData("https://api.deepseek.com/anthropic/", "https://api.deepseek.com/anthropic/v1/messages")]
    [InlineData("https://api.fireworks.ai/inference", "https://api.fireworks.ai/inference/v1/messages")]
    public void URLDaListaMontaOEndpoint(string url, string esperado)
    {
        ClienteProvedorIa.Endpoint(url)!.AbsoluteUri.Should().Be(esperado);
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.0.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("2002:a00:1::1", false)]
    [InlineData("2001:0:a00:1::1", false)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700::1111", true)]
    public void SoEnderecoPublicoConecta(string ip, bool publico)
    {
        ClienteProvedorIa.EnderecoPublico(IPAddress.Parse(ip)).Should().Be(publico);
    }

    [Fact]
    public void HandlerNaoSegueRedirectETemConnectCallback()
    {
        using var handler = ClienteProvedorIa.Handler();
        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseProxy.Should().BeFalse();
        handler.ConnectCallback.Should().NotBeNull();
    }

    private sealed record Capturada(string Uri, Dictionary<string, string> Headers, string Corpo);

    private sealed class FilaHandler(params (HttpStatusCode Status, string Corpo)[] respostas) : HttpMessageHandler
    {
        private int _proxima;
        public List<Capturada> Requisicoes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value));
            Requisicoes.Add(new(request.RequestUri!.AbsoluteUri, headers,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            var (status, corpo) = respostas[Math.Min(_proxima++, respostas.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class LogEmLista : ILogger<ClienteProvedorIa>
    {
        public List<string> Linhas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Linhas.Add(formatter(state, exception));
    }
}
