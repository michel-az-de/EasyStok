using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3365 (D2 da revisão #3271): o custo do turno é a soma do custo que cada resposta trouxe de quem a registrou em
/// <c>chamada_ia</c>. O laço não tem tabela de preço: resposta sem custo deixa o turno sem custo, nunca com zero.
/// </summary>
public class GeracaoAgenteCustoTests
{
    private static readonly DateTime Agora = new(2026, 11, 25, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();

    private static RespostaLlm Fim(decimal? custo) =>
        new RespostaLlm("end_turn", [new BlocoTextoLlm("ok")], 100, 20, 0, "claude-sonnet-5") { CustoUsd = custo };

    private static RespostaLlm PedeFerramenta(decimal? custo) =>
        new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("toolu_1", "consultar", JsonDocument.Parse("{}").RootElement.Clone())], 80, 10, 500,
            "claude-sonnet-5") { CustoUsd = custo };

    private static RespostaLlm Cortada(decimal? custo) =>
        new RespostaLlm(RespostaLlm.StopMaxTokens, [], 10, 4096, 0, "claude-sonnet-5") { CustoUsd = custo };

    private static IFerramentaAgente Consultar()
    {
        var ferramenta = Substitute.For<IFerramentaAgente>();
        ferramenta.Nome.Returns("consultar");
        ferramenta.Descricao.Returns("Consulta");
        ferramenta.SchemaJson.Returns("""{"type":"object","properties":{}}""");
        ferramenta.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns("""{"status":"EmPreparo"}""");
        return ferramenta;
    }

    private static IAgenteLlmClient Llm(params RespostaLlm[] respostas)
    {
        var llm = Substitute.For<IAgenteLlmClient>();
        var fila = new Queue<RespostaLlm>(respostas);
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ => fila.Dequeue());
        return llm;
    }

    private Task<GeracaoAgente> GerarAsync(IAgenteLlmClient llm, params IFerramentaAgente[] ferramentas) =>
        GeracaoAgenteAtendimento.GerarAsync(llm, "s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])],
            ferramentas.ToDictionary(f => f.Nome, StringComparer.Ordinal),
            new ContextoTurnoAgente(_empresaId, Conversa.Abrir(_empresaId, "5511988887777", Agora), Agora), 3,
            NullLogger.Instance, CancellationToken.None);

    [Fact]
    public async Task CustoDoTurnoSomaOCustoDeCadaResposta()
    {
        var geracao = await GerarAsync(Llm(PedeFerramenta(0.00054m), Fim(0.0006m)), Consultar());

        geracao.Consumo!.Passos.Should().Be(2);
        geracao.Consumo.CustoUsd.Should().Be(0.00114m);
    }

    [Fact]
    public async Task ChamadaCortadaQueRepeteSomaAsDuasNoCusto()
    {
        // #3273: a chamada cortada foi paga e a repetição também; as duas entram na conta do turno.
        var geracao = await GerarAsync(Llm(Cortada(0.0123m), Fim(0.0006m)));

        geracao.Consumo!.Passos.Should().Be(2);
        geracao.Consumo.CustoUsd.Should().Be(0.0129m);
    }

    [Fact]
    public async Task RespostaSemCustoDeixaOCustoDoTurnoNuloENaoSomaSoAsOutras()
    {
        var geracao = await GerarAsync(Llm(PedeFerramenta(0.00054m), Fim(custo: null)), Consultar());

        geracao.Consumo!.Passos.Should().Be(2);
        geracao.Consumo.CustoUsd.Should().BeNull("um custo parcial pareceria o custo do turno inteiro");
    }

    [Fact]
    public async Task PrimeiraRespostaSemCustoTambemDeixaOCustoNuloMesmoComAsSeguintesPrecificadas()
    {
        var geracao = await GerarAsync(Llm(PedeFerramenta(custo: null), Fim(0.0006m)), Consultar());

        geracao.Consumo!.CustoUsd.Should().BeNull();
    }

    [Fact]
    public async Task TurnoSemRespostaSomadaFicaSemCustoENuncaComZero()
    {
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new HttpRequestException("fora do ar", null, System.Net.HttpStatusCode.BadRequest));

        var geracao = await GerarAsync(llm);

        geracao.Consumo!.Passos.Should().Be(0);
        geracao.Consumo.CustoUsd.Should().BeNull();
    }

    [Fact]
    public async Task CustoZeroDeUmaRespostaGratuitaSomaComoZeroENaoViraNulo()
    {
        // Modelo com preço zero (cota de cortesia) é um custo conhecido: 0, diferente de "sem preço".
        var geracao = await GerarAsync(Llm(Fim(0m)));

        geracao.Consumo!.CustoUsd.Should().Be(0m);
    }
}
