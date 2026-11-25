using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3291 (R3 da revisão #3271): o turno tem prazo total, e o prazo só corta a chamada ao modelo. Ferramenta que já
/// começou e envio ao cliente terminam com o token de quem chamou, nunca com o do prazo.
/// </summary>
public class GeracaoAgentePrazoDoTurnoTests
{
    private static readonly DateTime Agora = new(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan PrazoCurto = TimeSpan.FromMilliseconds(40);
    private readonly Guid _empresaId = Guid.NewGuid();

    private static RespostaLlm Fim(string texto) => new("end_turn", [new BlocoTextoLlm(texto)], 10, 5);

    private static RespostaLlm PedeFerramenta(string nome) =>
        new(RespostaLlm.StopToolUse, [new BlocoUsoFerramentaLlm("toolu_1", nome, JsonDocument.Parse("{}").RootElement.Clone())], 10, 5);

    private static IFerramentaAgente Ferramenta(string nome, Func<CancellationToken, Task<string>> executar)
    {
        var ferramenta = Substitute.For<IFerramentaAgente>();
        ferramenta.Nome.Returns(nome);
        ferramenta.Descricao.Returns("descrição de " + nome);
        ferramenta.SchemaJson.Returns("""{"type":"object","properties":{}}""");
        ferramenta.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(c => executar(c.Arg<CancellationToken>()));
        return ferramenta;
    }

    /// <summary>Modelo que obedece ao token: só volta quando ele é cancelado.</summary>
    private static IAgenteLlmClient LlmQueNaoResponde(List<CancellationToken> tokens, TaskCompletionSource? chamou = null)
    {
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(async c =>
        {
            tokens.Add(c.Arg<CancellationToken>());
            chamou?.TrySetResult();
            await Task.Delay(Timeout.Infinite, c.Arg<CancellationToken>());
            return Fim("nunca");
        });
        return llm;
    }

    private Task<GeracaoAgente> GerarAsync(IAgenteLlmClient llm, TimeSpan? prazo, CancellationToken ct = default,
        params IFerramentaAgente[] ferramentas)
    {
        var conversa = Conversa.Abrir(_empresaId, "5511988887777", Agora);
        return GeracaoAgenteAtendimento.GerarAsync(llm, "s",
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])],
            ferramentas.ToDictionary(f => f.Nome, StringComparer.Ordinal),
            new ContextoTurnoAgente(_empresaId, conversa, Agora), 6, NullLogger.Instance, ct, prazoTotal: prazo);
    }

    [Fact]
    public async Task PrazoEstouradoNaChamadaAoModeloViraMotivoProprioEFalhaDoModeloNaoEhRegistrada()
    {
        var tokens = new List<CancellationToken>();

        var geracao = await GerarAsync(LlmQueNaoResponde(tokens), PrazoCurto).WaitAsync(TimeSpan.FromSeconds(10));

        geracao.Concluiu.Should().BeFalse();
        geracao.Texto.Should().BeNull();
        geracao.MotivoFalha.Should().StartWith(GeracaoAgenteAtendimento.MotivoPrazoDoTurno);
        geracao.Consumo!.FalhaLlm.Should().BeFalse("o prazo é do turno, não uma falha do provedor");
        MotivoDoTurno.DaFalhaDaGeracao(geracao.MotivoFalha!, geracao.Consumo.FalhaLlm).Should().Be(MotivoDoTurno.PrazoTurno);
        tokens.Should().ContainSingle("depois do prazo não há nova tentativa");
    }

    // #3347 (D4): o prazo vence mesmo quando o que chega depois dele é uma falha definitiva do provedor.
    [Fact]
    public async Task PrazoEstouradoVenceUmaFalhaDefinitivaQueChegaDepois()
    {
        var chamadas = 0;
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(async c =>
        {
            chamadas++;
            try { await Task.Delay(Timeout.Infinite, c.Arg<CancellationToken>()); }
            catch (OperationCanceledException) { throw new HttpRequestException("não autorizado", null, System.Net.HttpStatusCode.Unauthorized); }
            return Fim("nunca");
        });

        var geracao = await GerarAsync(llm, PrazoCurto).WaitAsync(TimeSpan.FromSeconds(10));

        geracao.MotivoFalha.Should().StartWith(GeracaoAgenteAtendimento.MotivoPrazoDoTurno);
        geracao.Consumo!.FalhaLlm.Should().BeFalse();
        geracao.Consumo.FalhaLlmDefinitiva.Should().BeFalse();
        chamadas.Should().Be(1, "o 401 saiu da chamada que o prazo cortou: sem ela o teste não prova nada");
        MotivoDoTurno.DaFalhaDaGeracao(geracao.MotivoFalha!, geracao.Consumo.FalhaLlm, geracao.Consumo.FalhaLlmDefinitiva).Should().Be(MotivoDoTurno.PrazoTurno);
    }

    [Fact]
    public async Task FerramentaEmCursoTerminaComOTokenDeQuemChamouEOProximoModeloNaoEhChamadoDepoisDoPrazo()
    {
        var externo = new CancellationTokenSource();
        CancellationToken? tokenDaFerramenta = null;
        var terminou = false;
        var ferramenta = Ferramenta("consultar_pedido", async token =>
        {
            tokenDaFerramenta = token;
            await Task.Delay(400, token);
            terminou = true;
            return """{"status":"EmPreparo"}""";
        });
        var chamadas = 0;
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            chamadas++;
            return Task.FromResult(chamadas == 1 ? PedeFerramenta("consultar_pedido") : Fim("segue"));
        });

        var geracao = await GerarAsync(llm, PrazoCurto, externo.Token, ferramenta).WaitAsync(TimeSpan.FromSeconds(10));

        terminou.Should().BeTrue("a ferramenta que já começou termina");
        tokenDaFerramenta.Should().Be(externo.Token, "o prazo do turno não corta ferramenta");
        chamadas.Should().Be(1, "o prazo já tinha vencido quando a ferramenta voltou");
        geracao.Tokens.Should().Be(10 + 5, "a volta que terminou foi paga");
        geracao.Concluiu.Should().BeFalse();
        geracao.MotivoFalha.Should().StartWith(GeracaoAgenteAtendimento.MotivoPrazoDoTurno);
        geracao.Consumo!.Ferramentas.Should().Equal(new FerramentaDoTurno("consultar_pedido", true));
    }

    [Fact]
    public async Task DesligamentoDoServicoSobeComoCancelamentoENaoViraPrazo()
    {
        var chamou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var externo = new CancellationTokenSource();
        var geracao = GerarAsync(LlmQueNaoResponde([], chamou), TimeSpan.FromMinutes(5), externo.Token);
        await chamou.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await externo.CancelAsync();

        var act = async () => await geracao.WaitAsync(TimeSpan.FromSeconds(10));
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task SemPrazoOuComPrazoZeroOTurnoNaoTemLimiteDeTempo(int? segundos)
    {
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Delay(150);
            return Fim("pronto");
        });

        var geracao = await GerarAsync(llm, segundos is null ? null : TimeSpan.FromSeconds(segundos.Value));

        geracao.Concluiu.Should().BeTrue();
        geracao.Texto.Should().Be("pronto");
    }

    [Fact]
    public async Task ChamadaDentroDoPrazoRespondeNormalmente()
    {
        var geracao = await GerarAsync(
            Substitute.For<IAgenteLlmClient>().ComResposta(Fim("Oi! Como posso ajudar?")), TimeSpan.FromMinutes(3));

        geracao.Concluiu.Should().BeTrue();
        geracao.MotivoFalha.Should().BeNull();
        geracao.Texto.Should().Be("Oi! Como posso ajudar?");
    }
}

internal static class LlmDeTesteExtensions
{
    public static IAgenteLlmClient ComResposta(this IAgenteLlmClient llm, RespostaLlm resposta)
    {
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(resposta);
        return llm;
    }
}
