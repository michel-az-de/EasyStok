using System.Net;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Domain.Entities.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3273 (R1, R2 da revisão #3271): estourar <c>max_tokens</c> é desfecho próprio, nunca resposta final; e a nova tentativa
/// só repete falha transitória. #3347 (D4): a mesma regra separa, no motivo do turno, a falha definitiva da transitória.
/// </summary>
public class GeracaoAgenteTetoESaidaTests
{
    private static readonly DateTime Agora = new(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();

    // #3347 (D4): a frase para a equipe é a mesma nas duas classes de falha; fica como literal para fixar que não muda.
    private const string FraseDaFalhaDoLlm = "o agente não conseguiu responder (falha ao chamar o LLM)";

    private static RespostaLlm Fim(string texto) => new("end_turn", [new BlocoTextoLlm(texto)], 10, 5);

    private static RespostaLlm Cortada(string texto) =>
        new(RespostaLlm.StopMaxTokens, texto.Length == 0 ? [] : [new BlocoTextoLlm(texto)], 10, 4096);

    private static RespostaLlm Recusa(string texto) =>
        new(RespostaLlm.StopRefusal, texto.Length == 0 ? [] : [new BlocoTextoLlm(texto)], 10, 20);

    private ContextoTurnoAgente NovoContexto() => new(_empresaId, Conversa.Abrir(_empresaId, "5511988887777", Agora), Agora);

    private Task<GeracaoAgente> GerarAsync(IAgenteLlmClient llm, ContextoTurnoAgente? contexto = null) =>
        GeracaoAgenteAtendimento.GerarAsync(llm, "s",
            [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], new Dictionary<string, IFerramentaAgente>(),
            contexto ?? NovoContexto(), 3, NullLogger.Instance, CancellationToken.None);

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

    [Fact]
    public async Task TetoJaNoMaximoNaoRepeteECaiNoMotivoProprio()
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => new RespostaLlm(RespostaLlm.StopMaxTokens, [new BlocoTextoLlm("corta")], 10,
            GeracaoAgenteAtendimento.MaxTokensNovaTentativa));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(1, "repetir com o mesmo teto só dobraria o custo");
        geracao.Concluiu.Should().BeFalse();
        geracao.MotivoFalha.Should().StartWith(GeracaoAgenteAtendimento.MotivoTetoDeSaida);
    }

    [Fact]
    public async Task TetoJaNoMaximoDoModeloNaoRepeteMesmoAbaixoDoPiso()
    {
        // #3313: com saida_max cadastrado o teto usado É o máximo do modelo; pedir 16384 não o sobe e a repetição sairia igual.
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => Cortada("corta") with { TetoNoMaximo = true });

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(1, "repetir com o mesmo teto só dobraria o custo");
        geracao.Concluiu.Should().BeFalse();
        geracao.Texto.Should().BeNull();
        geracao.MotivoFalha.Should().StartWith(GeracaoAgenteAtendimento.MotivoTetoDeSaida);
        geracao.Tokens.Should().Be(10 + 4096, "a chamada cortada foi paga");
        MotivoDoTurno.DaFalhaDaGeracao(geracao.MotivoFalha!, geracao.Consumo!.FalhaLlm).Should().Be(MotivoDoTurno.TetoSaida);
    }

    [Theory]
    [InlineData("Não posso ajudar com isso.")]
    [InlineData("")]
    public async Task RecusaDoModeloNaoVaiAoClienteNemRepeteEEscalaComMotivoProprio(string texto)
    {
        // #3321 (D4): o texto da recusa não é resposta, e repetir o mesmo pedido tende a repetir a recusa.
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => Recusa(texto));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(1);
        geracao.Concluiu.Should().BeFalse();
        geracao.Texto.Should().BeNull("o texto da recusa nunca vai ao cliente");
        geracao.MotivoFalha.Should().StartWith(GeracaoAgenteAtendimento.MotivoRecusa);
        geracao.Tokens.Should().Be(10 + 20, "a chamada recusada foi paga");
        MotivoDoTurno.DaFalhaDaGeracao(geracao.MotivoFalha!, geracao.Consumo!.FalhaLlm).Should().Be(MotivoDoTurno.RecusaModelo);
    }

    [Fact]
    public async Task RecusaDepoisDeEnvioNoTurnoLimpaARespostaVerificadaEAindaEscala()
    {
        // #3321: texto vazio depois de um envio é o fim normal do turno; recusa não é. A equipe confere e a resposta verificada não sai.
        var contexto = NovoContexto();
        contexto.RegistrarRespostaVerificada("Seu pedido sai às 12h.");
        contexto.RegistrarEnvio();

        var geracao = await GerarAsync(Llm([], () => Recusa("")), contexto);

        geracao.Concluiu.Should().BeFalse();
        geracao.MotivoFalha.Should().StartWith(GeracaoAgenteAtendimento.MotivoRecusa);
        contexto.RespostaVerificada.Should().BeNull();
    }

    [Fact]
    public async Task ChamadaDeTetoMaiorQueFalhaNaoRepete()
    {
        var capturadas = new List<RequisicaoLlm>();
        var chamadas = 0;
        var llm = Llm(capturadas, () => ++chamadas == 1 ? Cortada("") : throw new HttpRequestException("prazo", null, HttpStatusCode.GatewayTimeout));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(2, "a de teto maior não se repete: o prazo seria estourado de novo");
        geracao.Consumo!.FalhaLlm.Should().BeTrue();
        geracao.Consumo.FalhaLlmDefinitiva.Should().BeFalse("504 é transitória");
        geracao.Tokens.Should().Be(10 + 4096, "a chamada cortada foi paga");
    }

    // #3347 (D4): a chamada de teto maior fica fora da nova tentativa e cai no mesmo catch: a classe vale nela também.
    [Fact]
    public async Task ChamadaDeTetoMaiorQueFalhaComErroDefinitivoGravaADefinitiva()
    {
        var capturadas = new List<RequisicaoLlm>();
        var chamadas = 0;
        var llm = Llm(capturadas, () => ++chamadas == 1 ? Cortada("") : throw new HttpRequestException("sem crédito", null, HttpStatusCode.PaymentRequired));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(2);
        geracao.Consumo!.FalhaLlm.Should().BeTrue();
        geracao.Consumo.FalhaLlmDefinitiva.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData((HttpStatusCode)499)]
    public async Task FalhaDefinitivaNaoRepeteEGravaADefinitiva(HttpStatusCode status)
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => throw new HttpRequestException("definitivo", null, status));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(1);
        geracao.Concluiu.Should().BeFalse();
        geracao.MotivoFalha.Should().Be(FraseDaFalhaDoLlm);
        geracao.Consumo!.FalhaLlm.Should().BeTrue("o desfecho Erro depende dele");
        geracao.Consumo.FalhaLlmDefinitiva.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FalhaTransitoriaRepeteUmaVezEContinuaTransitoria(HttpStatusCode status)
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => throw new HttpRequestException("blip", null, status));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(2);
        geracao.MotivoFalha.Should().Be(FraseDaFalhaDoLlm);
        geracao.Consumo!.FalhaLlm.Should().BeTrue();
        geracao.Consumo.FalhaLlmDefinitiva.Should().BeFalse();
    }

    [Fact]
    public async Task FalhaDeRedeSemStatusRepeteUmaVezEContinuaTransitoria()
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => throw new HttpRequestException("connection reset"));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(2);
        geracao.Consumo!.FalhaLlm.Should().BeTrue();
        geracao.Consumo.FalhaLlmDefinitiva.Should().BeFalse();
    }

    // Resposta ilegível, tempo esgotado e afins não trazem status: a nova tentativa os repete e a classe é a transitória.
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(TimeoutException))]
    public async Task ExcecaoSemStatusRepeteUmaVezEContinuaTransitoria(Type tipo)
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => throw (Exception)Activator.CreateInstance(tipo, "sem status")!);

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(2);
        geracao.Consumo!.FalhaLlm.Should().BeTrue();
        geracao.Consumo.FalhaLlmDefinitiva.Should().BeFalse();
    }

    // A falha que chega ao fim é a que decide: o 503 repete uma vez e o 401 da repetição é definitivo.
    [Fact]
    public async Task FalhaTransitoriaSeguidaDeDefinitivaGravaADefinitiva()
    {
        var capturadas = new List<RequisicaoLlm>();
        var chamadas = 0;
        var llm = Llm(capturadas, () => throw new HttpRequestException("x", null,
            ++chamadas == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Unauthorized));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(2);
        geracao.Consumo!.FalhaLlmDefinitiva.Should().BeTrue();
    }

    // Sem chave, cota estourada ou disjuntor aberto: não traz status, não se repete e segue falha_llm (a razão fica fora desta classe).
    [Fact]
    public async Task ProvedorIndisponivelNaoRepeteENaoEhDefinitivo()
    {
        var capturadas = new List<RequisicaoLlm>();
        var llm = Llm(capturadas, () => throw new ProvedorIaIndisponivelException("cota estourada"));

        var geracao = await GerarAsync(llm);

        capturadas.Should().HaveCount(1);
        geracao.Consumo!.FalhaLlm.Should().BeTrue();
        geracao.Consumo.FalhaLlmDefinitiva.Should().BeFalse();
    }
}
