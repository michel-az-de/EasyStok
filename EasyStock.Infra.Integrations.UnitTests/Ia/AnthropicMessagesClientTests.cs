using System.Net;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Infra.Integrations.Ia;
using FluentAssertions;
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
        return new AnthropicMessagesClient(http, Options.Create(opcoes), NullLogger<AnthropicMessagesClient>.Instance);
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
    public async Task ErroHttpLancaSemVazarChave()
    {
        var client = Criar(new CapturaHandler(HttpStatusCode.TooManyRequests, """{"type":"error","error":{"type":"rate_limit_error"}}"""));

        var acao = () => client.EnviarAsync(new RequisicaoLlm("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], []));

        var ex = await acao.Should().ThrowAsync<HttpRequestException>();
        ex.Which.Message.Should().Contain("429").And.NotContain("chave-de-teste");
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
