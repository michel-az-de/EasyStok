using System.Net;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3273 (R1, R2 da revisão #3271): estourar <c>max_tokens</c> é desfecho próprio, nunca resposta final; e a nova tentativa
/// só repete falha transitória.
/// </summary>
public class GeracaoAgenteTetoESaidaTests
{
    private static readonly DateTime Agora = new(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();

    private static RespostaLlm Fim(string texto) => new("end_turn", [new BlocoTextoLlm(texto)], 10, 5);

    private static RespostaLlm Cortada(string texto) =>
        new(RespostaLlm.StopMaxTokens, texto.Length == 0 ? [] : [new BlocoTextoLlm(texto)], 10, 4096);

    private Task<GeracaoAgente> GerarAsync(IAgenteLlmClient llm)
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);
        return GeracaoAgenteAtendimento.GerarAsync(llm, "s",
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], new Dictionary<string, IFerramentaAgente>(),
            new ContextoTurnoAgente(_empresaId, conversa, Agora), 3, NullLogger.Instance, CancellationToken.None);
    }

    private static IAgenteLlmClient Llm(List<RequisicaoLlm> capturadas, params Func<RespostaLlm>[] respostas)
    {
        var llm = Substitute.For<IAgenteLlmClient>();
        var indice = 0;
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(x =>
        {
            capturadas.Add(x.Arg<RequisicaoLlm>());
            return respostas[Math.Min(indice++, respostas.Length - 1)]();
        });
        return llm;
    }

    [Fact]
    public async Task TetoEstouradoComTextoVazioTentaComTetoMaiorENaoEscalaSePassar()
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => Cortada(""), () => Fim("Oi! Como posso ajudar?"));

        var geracao = await GerarAsync(llm);

        geracao.Concluiu.Should().BeTrue();
        geracao.MotivoFalha.Should().BeNull();
        geracao.Texto.Should().Be("Oi! Como posso ajudar?");
        capturadas.Should().HaveCount(2);
        capturadas[0].MaxTokensMinimo.Should().BeNull();
        capturadas[1].MaxTokensMinimo.Should().Be(GeracaoAgenteAtendimento.MaxTokensNovaTentativa);
        // Os dois lados da conta: a chamada cortada também foi paga.
        geracao.Tokens.Should().Be(10 + 4096 + 10 + 5);
    }

    [Fact]
    public async Task TextoCortadoNuncaVaiAoClienteEAConversaVaiParaAEquipe()
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => Cortada("Para o seu pedido, a entrega de amanhã fica combi"));

        var geracao = await GerarAsync(llm);

        geracao.Concluiu.Should().BeFalse();
        geracao.Texto.Should().BeNull();
        geracao.MotivoFalha.Should().StartWith(GeracaoAgenteAtendimento.MotivoTetoDeSaida);
        capturadas.Should().HaveCount(2, "uma nova tentativa com teto maior e só");
    }

    [Fact]
    public async Task TetoEstouradoNasDuasChamadasGravaOMotivoPropriodoTurno()
    {
        var geracao = await GerarAsync(Llm([], () => Cortada("")));

        MotivoDoTurno.DaFalhaDaGeracao(geracao.MotivoFalha!, geracao.Consumo!.FalhaLlm).Should().Be(MotivoDoTurno.TetoSaida);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task FalhaDefinitivaNaoRepete(HttpStatusCode status)
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => throw new HttpRequestException("definitivo", null, status));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(1);
        geracao.Concluiu.Should().BeFalse();
        geracao.Consumo!.FalhaLlm.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FalhaTransitoriaRepeteUmaVez(HttpStatusCode status)
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => throw new HttpRequestException("blip", null, status));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(2);
        geracao.Consumo!.FalhaLlm.Should().BeTrue();
    }

    [Fact]
    public async Task FalhaDeRedeSemStatusRepeteUmaVez()
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => throw new HttpRequestException("connection reset"));

        await GerarAsync(llm);

        capturadas.Should().HaveCount(2);
    }
}
