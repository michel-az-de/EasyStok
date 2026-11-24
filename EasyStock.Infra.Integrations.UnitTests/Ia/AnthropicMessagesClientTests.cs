using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Infra.Integrations.Ia;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.UnitTests.Ia;

/// <summary>HTTP stubado por <see cref="CapturaHandler"/>: nenhuma chamada de rede real, nenhuma chave real.</summary>
public class AnthropicMessagesClientTests
{
    private const string RespostaToolUse = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5",
         "content":[
           {"type":"thinking","thinking":"","signature":"sig-abc"},
           {"type":"text","text":"Vou verificar."},
           {"type":"tool_use","id":"toolu_1","name":"consultar_pedido","input":{"pedido_id":"p-1"}}],
         "stop_reason":"tool_use","usage":{"input_tokens":120,"output_tokens":30}}
        """;

    private static AnthropicMessagesClient Criar(CapturaHandler handler, AnthropicAgenteOptions? opcoes = null)
    {
        opcoes ??= new AnthropicAgenteOptions { Enabled = true, ApiKey = "chave-de-teste", ModeloAgente = "claude-sonnet-5" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://anthropic.test/") };
        return new AnthropicMessagesClient(http, Options.Create(opcoes), PipelinesDeTeste.Reais(), NullLogger<AnthropicMessagesClient>.Instance);
    }

    [Fact]
    public async Task MontaCorpoComTools()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, RespostaToolUse);
        var client = Criar(handler);
        var entradaAnterior = JsonDocument.Parse("""{"x":1}""").RootElement;

        var resposta = await client.EnviarAsync(new RequisicaoLlm(
            "Você é o atendente.",
            [
                new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("quanto tempo falta?")]),
                new MensagemLlm(MensagemLlm.Assistente, [new BlocoUsoFerramentaLlm("toolu_0", "consultar_cardapio", entradaAnterior)]),
                new MensagemLlm(MensagemLlm.Usuario, [new BlocoResultadoFerramentaLlm("toolu_0", "[]", EhErro: true)])
            ],
            [new FerramentaLlm("consultar_pedido", "Consulta o pedido", """{"type":"object","properties":{"pedido_id":{"type":"string"}}}""")]));

        handler.Requisicao!.RequestUri!.AbsoluteUri.Should().Be("https://anthropic.test/v1/messages");
        handler.Requisicao.Headers.GetValues("x-api-key").Should().ContainSingle("chave-de-teste");
        handler.Requisicao.Headers.GetValues("anthropic-version").Should().ContainSingle("2023-06-01");

        using var corpo = JsonDocument.Parse(handler.Corpo!);
        var raiz = corpo.RootElement;
        raiz.GetProperty("model").GetString().Should().Be("claude-sonnet-5");
        raiz.GetProperty("max_tokens").GetInt32().Should().BePositive();
        raiz.GetProperty("system").GetString().Should().Be("Você é o atendente.");

        var ferramenta = raiz.GetProperty("tools")[0];
        ferramenta.GetProperty("name").GetString().Should().Be("consultar_pedido");
        ferramenta.GetProperty("description").GetString().Should().Be("Consulta o pedido");
        ferramenta.GetProperty("input_schema").GetProperty("type").GetString().Should().Be("object");

        var mensagens = raiz.GetProperty("messages");
        mensagens.GetArrayLength().Should().Be(3);
        mensagens[0].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("text");
        var usoAnterior = mensagens[1].GetProperty("content")[0];
        usoAnterior.GetProperty("type").GetString().Should().Be("tool_use");
        usoAnterior.GetProperty("input").GetProperty("x").GetInt32().Should().Be(1);
        var resultado = mensagens[2].GetProperty("content")[0];
        resultado.GetProperty("type").GetString().Should().Be("tool_result");
        resultado.GetProperty("tool_use_id").GetString().Should().Be("toolu_0");
        resultado.GetProperty("content").GetString().Should().Be("[]");
        resultado.GetProperty("is_error").GetBoolean().Should().BeTrue();

        resposta.StopReason.Should().Be("tool_use");
        resposta.TokensEntrada.Should().Be(120);
        resposta.TokensSaida.Should().Be(30);
        resposta.Conteudo[0].Should().BeOfType<BlocoOpacoLlm>();
        resposta.Conteudo[1].Should().Be(new BlocoTextoLlm("Vou verificar."));
        var uso = resposta.Conteudo[2].Should().BeOfType<BlocoUsoFerramentaLlm>().Subject;
        uso.Id.Should().Be("toolu_1");
        uso.Nome.Should().Be("consultar_pedido");
        uso.Entrada.GetProperty("pedido_id").GetString().Should().Be("p-1");
    }

    [Fact]
    public async Task BlocoOpacoVoltaInalterado()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, RespostaToolUse);
        var client = Criar(handler);
        var primeira = await client.EnviarAsync(new RequisicaoLlm("s",
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], []));

        await client.EnviarAsync(new RequisicaoLlm("s",
        [
            new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")]),
            new MensagemLlm(MensagemLlm.Assistente, primeira.Conteudo),
            new MensagemLlm(MensagemLlm.Usuario, [new BlocoResultadoFerramentaLlm("toolu_1", "ok")])
        ], []));

        using var corpo = JsonDocument.Parse(handler.Corpo!);
        var thinking = corpo.RootElement.GetProperty("messages")[1].GetProperty("content")[0];
        thinking.GetProperty("type").GetString().Should().Be("thinking");
        thinking.GetProperty("signature").GetString().Should().Be("sig-abc");
        corpo.RootElement.TryGetProperty("tools", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, "chave")]
    [InlineData(true, "")]
    public void IndisponivelDesligadoOuSemChave(bool enabled, string chave)
    {
        var client = Criar(new CapturaHandler(HttpStatusCode.OK, "{}"),
            new AnthropicAgenteOptions { Enabled = enabled, ApiKey = chave });

        client.Disponivel.Should().BeFalse();
    }

    [Fact]
    public async Task ChaveDoAgenteSubstituiChaveCompartilhada()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, RespostaToolUse);
        var client = Criar(handler, new AnthropicAgenteOptions
        {
            Enabled = true, ApiKey = "chave-anthropic", ApiKeyAgente = "chave-do-agente", ModeloAgente = "m"
        });

        await client.EnviarAsync(new RequisicaoLlm("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], []));

        handler.Requisicao!.Headers.GetValues("x-api-key").Should().ContainSingle("chave-do-agente");
    }

    [Fact]
    public void ChaveDoAgenteBastaParaFicarDisponivel()
    {
        var client = Criar(new CapturaHandler(HttpStatusCode.OK, "{}"),
            new AnthropicAgenteOptions { Enabled = true, ApiKey = "", ApiKeyAgente = "chave-do-agente" });

        client.Disponivel.Should().BeTrue();
    }

    [Fact]
    public async Task AutenticacaoBearerEnviaAuthorizationSemXApiKey()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, RespostaToolUse);
        var client = Criar(handler, new AnthropicAgenteOptions
        {
            Enabled = true, ApiKeyAgente = "chave-do-agente", AutenticacaoBearer = true, ModeloAgente = "m"
        });

        await client.EnviarAsync(new RequisicaoLlm("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], []));

        handler.Requisicao!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requisicao.Headers.Authorization.Parameter.Should().Be("chave-do-agente");
        handler.Requisicao.Headers.Contains("x-api-key").Should().BeFalse();
    }

    [Fact]
    public async Task ErroHttpLancaSemVazarChave()
    {
        var client = Criar(new CapturaHandler(HttpStatusCode.TooManyRequests, """{"type":"error","error":{"type":"rate_limit_error"}}"""));

        var acao = () => client.EnviarAsync(new RequisicaoLlm("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], []));

        var ex = await acao.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("429").And.NotContain("chave-de-teste");
    }

    private const string RespostaComCache = """
        {"id":"msg_3","type":"message","role":"assistant","model":"claude-sonnet-5",
         "content":[{"type":"text","text":"Oi"}],"stop_reason":"end_turn",
         "usage":{"input_tokens":40,"output_tokens":8,"cache_creation_input_tokens":1200,"cache_read_input_tokens":900}}
        """;

    [Fact]
    public async Task SystemVolatilVaiEmOutroBlocoEBreakpointSoNoEstavel()
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, RespostaToolUse);
        var client = Criar(handler);

        await client.EnviarAsync(new RequisicaoLlm("Prompt estável.",
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], [],
            SystemVolatil: "Agora: 19/11/2026 10:00"));

        using var corpo = JsonDocument.Parse(handler.Corpo!);
        var system = corpo.RootElement.GetProperty("system");
        system.ValueKind.Should().Be(JsonValueKind.Array);
        system.GetArrayLength().Should().Be(2);
        system[0].GetProperty("type").GetString().Should().Be("text");
        system[0].GetProperty("text").GetString().Should().Be("Prompt estável.");
        system[0].GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        system[1].GetProperty("type").GetString().Should().Be("text");
        system[1].GetProperty("text").GetString().Should().Be("Agora: 19/11/2026 10:00");
        system[1].TryGetProperty("cache_control", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SemSystemVolatilCorpoFicaComSystemTexto(string? volatil)
    {
        var handler = new CapturaHandler(HttpStatusCode.OK, RespostaToolUse);
        var client = Criar(handler);

        await client.EnviarAsync(new RequisicaoLlm("Você é o atendente.",
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], [], SystemVolatil: volatil));

        using var corpo = JsonDocument.Parse(handler.Corpo!);
        corpo.RootElement.GetProperty("system").ValueKind.Should().Be(JsonValueKind.String);
        corpo.RootElement.GetProperty("system").GetString().Should().Be("Você é o atendente.");
    }

    [Fact]
    public async Task LeCacheEscritaDaResposta()
    {
        var client = Criar(new CapturaHandler(HttpStatusCode.OK, RespostaComCache));

        var resposta = await client.EnviarAsync(RequisicaoSimples());

        resposta.TokensCacheEscrito.Should().Be(1200);
        resposta.TokensCacheLido.Should().Be(900);
    }

    [Fact]
    public async Task RegistraUsoDaRespostaSemTextoDaMensagem()
    {
        var logger = new CapturaLogger();
        var http = new HttpClient(new CapturaHandler(HttpStatusCode.OK, RespostaComCache)) { BaseAddress = new Uri("https://anthropic.test/") };
        var client = new AnthropicMessagesClient(http,
            Options.Create(new AnthropicAgenteOptions { Enabled = true, ApiKey = "chave-de-teste", ModeloAgente = "claude-sonnet-5" }),
            PipelinesDeTeste.Reais(), logger);

        await client.EnviarAsync(new RequisicaoLlm("s",
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("texto-secreto-do-cliente")])], [], SystemVolatil: "v"));

        var linha = logger.Mensagens.Should().ContainSingle(m => m.StartsWith("Anthropic uso")).Subject;
        linha.Should().Contain("modelo=claude-sonnet-5").And.Contain("entrada=40").And.Contain("saida=8")
            .And.Contain("cache_escrito=1200").And.Contain("cache_lido=900");
        linha.Should().NotContain("texto-secreto-do-cliente");
    }

    private sealed class CapturaLogger : ILogger<AnthropicMessagesClient>
    {
        public List<string> Mensagens { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Mensagens.Add(formatter(state, exception));
    }

    private static RequisicaoLlm RequisicaoSimples() =>
        new("Você é o atendente.", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], []);

    [Fact]
    public async Task DevolveOModeloQueRespondeuEOCacheLido()
    {
        // S67 (#2534): o registro do turno guarda o modelo que respondeu e o cache lido, que tem preço próprio.
        const string corpo = """
            {"id":"msg_2","type":"message","role":"assistant","model":"claude-sonnet-5-20261101",
             "content":[{"type":"text","text":"Oi"}],"stop_reason":"end_turn",
             "usage":{"input_tokens":40,"output_tokens":8,"cache_read_input_tokens":900}}
            """;

        var resposta = await Criar(new CapturaHandler(HttpStatusCode.OK, corpo)).EnviarAsync(RequisicaoSimples());

        resposta.Modelo.Should().Be("claude-sonnet-5-20261101");
        resposta.TokensEntrada.Should().Be(40);
        resposta.TokensSaida.Should().Be(8);
        resposta.TokensCacheLido.Should().Be(900);
    }

    [Fact]
    public async Task ModeloDaRequisicaoSubstituiOConfigurado()
    {
        // S68 (#2597): o turno pede o modelo da loja, já conferido na lista aprovada; sem ele, vale o configurado.
        var handler = new CapturaHandler(HttpStatusCode.OK, """{"content":[],"stop_reason":"end_turn"}""");

        var resposta = await Criar(handler).EnviarAsync(RequisicaoSimples() with { Modelo = "claude-haiku-5" });

        using var corpo = JsonDocument.Parse(handler.Corpo!);
        corpo.RootElement.GetProperty("model").GetString().Should().Be("claude-haiku-5");
        resposta.Modelo.Should().Be("claude-haiku-5");
    }

    [Fact]
    public async Task SemModeloNaRespostaFicaOConfiguradoESemCacheZero()
    {
        var resposta = await Criar(new CapturaHandler(HttpStatusCode.OK, """{"content":[],"stop_reason":"end_turn"}"""))
            .EnviarAsync(RequisicaoSimples());

        resposta.Modelo.Should().Be("claude-sonnet-5");
        resposta.TokensCacheLido.Should().Be(0);
    }

    [Fact]
    public async Task PisoDaRequisicaoSobeOTetoDeSaidaMasNuncaOBaixa()
    {
        // #3273 (R1): a nova tentativa depois de max_tokens pede teto maior; configuração maior que o piso vale.
        var handler = new CapturaHandler(HttpStatusCode.OK, """{"content":[],"stop_reason":"end_turn"}""");
        var cliente = Criar(handler, new AnthropicAgenteOptions { Enabled = true, ApiKey = "k", MaxTokensAgente = 4096 });

        await cliente.EnviarAsync(RequisicaoSimples() with { MaxTokensMinimo = 16384 });
        using (var corpo = JsonDocument.Parse(handler.Corpo!))
            corpo.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(16384);

        await cliente.EnviarAsync(RequisicaoSimples());
        using (var corpo = JsonDocument.Parse(handler.Corpo!))
            corpo.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(4096);

        var grande = Criar(handler, new AnthropicAgenteOptions { Enabled = true, ApiKey = "k", MaxTokensAgente = 32000 });
        await grande.EnviarAsync(RequisicaoSimples() with { MaxTokensMinimo = 16384 });
        using (var corpo = JsonDocument.Parse(handler.Corpo!))
            corpo.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(32000);
    }

    [Fact]
    public async Task CorpoDeErroNaoLevaAChaveParaOLog()
    {
        // #3273 (R12): o provedor do agente pode ecoar a chave; o log sai sanitizado, como no caminho por loja.
        var logger = new CapturaLogger();
        var http = new HttpClient(new CapturaHandler(HttpStatusCode.Unauthorized, """{"error":"chave invalida: chave-secreta-xyz"}"""))
        { BaseAddress = new Uri("https://anthropic.test/") };
        var client = new AnthropicMessagesClient(http,
            Options.Create(new AnthropicAgenteOptions { Enabled = true, ApiKey = "chave-secreta-xyz", ModeloAgente = "claude-sonnet-5" }),
            PipelinesDeTeste.Reais(), logger);

        var act = () => client.EnviarAsync(RequisicaoSimples());

        var erro = await act.Should().ThrowAsync<HttpRequestException>();
        erro.Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        logger.Mensagens.Should().NotBeEmpty().And.OnlyContain(m => !m.Contains("chave-secreta-xyz"));
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
