using System.Net;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Ia;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.Services.Ia;

/// <summary>#3063 (S2a de #2948): cada chamada ao LLM vira uma linha de chamada_ia, sem mudar a resposta.</summary>
public class RegistroChamadaIaLlmClientTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private readonly IAgenteLlmClient _interno = Substitute.For<IAgenteLlmClient>();
    private readonly IRegistroChamadaIa _registro = Substitute.For<IRegistroChamadaIa>();
    private readonly List<ChamadaIa> _gravadas = [];

    public RegistroChamadaIaLlmClientTests()
    {
        _registro.RegistrarAsync(Arg.Do<ChamadaIa>(_gravadas.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    // #3365 (D2): a tabela Anthropic:Precos agora vale na chamada. O modelo "deepseek-flash" dos testes antigos fica sem preço.
    private static IOptions<PrecosModelosAgenteOptions> Precos()
    {
        var tabela = new PrecosModelosAgenteOptions();
        tabela.Modelos["claude-sonnet-5"] = new PrecoModeloAgente(3m, 15m, 0.3m);
        return Options.Create(tabela);
    }

    private RegistroChamadaIaLlmClient Cliente() =>
        new(_interno, _registro, new FakeTimeProvider(new DateTimeOffset(2026, 11, 21, 17, 0, 0, TimeSpan.Zero)),
            NullLogger<RegistroChamadaIaLlmClient>.Instance, Precos());

    private static RequisicaoLlm Requisicao(FinalidadeIa finalidade = FinalidadeIa.ResumoCliente, Guid? empresa = null) =>
        new("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], [])
        {
            EmpresaId = empresa ?? Empresa,
            Finalidade = finalidade,
        };

    [Fact]
    public async Task SucessoGravaTokensModeloEFinalidadeEDevolveAMesmaResposta()
    {
        var resposta = new RespostaLlm("end_turn", [new BlocoTextoLlm("ok")], 100, 20, 5, "deepseek-flash", 3);
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(resposta);

        var devolvida = await Cliente().EnviarAsync(Requisicao());

        devolvida.Should().BeSameAs(resposta, "sem preço para o modelo a resposta passa intacta, sem custo");
        var c = _gravadas.Should().ContainSingle().Subject;
        c.EmpresaId.Should().Be(Empresa);
        c.Finalidade.Should().Be(FinalidadeIa.ResumoCliente);
        c.Desfecho.Should().Be(DesfechoChamadaIa.Ok);
        (c.TokensEntrada, c.TokensSaida, c.TokensCacheLido, c.TokensCacheEscrito).Should().Be((100, 20, 5, 3));
        c.ModeloRespondido.Should().Be("deepseek-flash");
        c.ProvedorId.Should().BeNull();
        c.ModeloId.Should().BeNull();
        c.CustoUsd.Should().BeNull();
    }

    private const string ModeloComPreco = "claude-sonnet-5-20261101";

    [Fact]
    public async Task ComPrecoGravaOCustoNaLinhaEDevolveOMesmoCustoAoTurno()
    {
        // #3365 (D2): o custo nasce aqui, uma vez. Entrada + escrita de cache, como o turno fazia:
        // (180 + 1200) × 3 + 30 × 15 + 500 × 0,30, por milhão = 0,00474.
        var resposta = new RespostaLlm("end_turn", [new BlocoTextoLlm("ok")], 180, 30, 500, ModeloComPreco, 1200);
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(resposta);

        var devolvida = await Cliente().EnviarAsync(Requisicao(FinalidadeIa.Atendimento));

        _gravadas.Should().ContainSingle().Which.CustoUsd.Should().Be(0.00474m);
        devolvida.CustoUsd.Should().Be(0.00474m);
        devolvida.Should().Be(resposta with { CustoUsd = 0.00474m }, "só o custo muda na resposta");
    }

    [Fact]
    public async Task CustoSaiArredondadoEmSeisCasasComoALinha()
    {
        // 7 tokens de cache lido × 0,30 / 1M = 0,0000021: a linha guarda 0,000002 e é esse o valor que o turno soma.
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [], 0, 0, 7, ModeloComPreco));

        var devolvida = await Cliente().EnviarAsync(Requisicao(FinalidadeIa.Atendimento));

        _gravadas.Should().ContainSingle().Which.CustoUsd.Should().Be(0.000002m);
        devolvida.CustoUsd.Should().Be(0.000002m);
    }

    [Fact]
    public async Task ModeloSemPrecoDevolveCustoNuloENuncaZero()
    {
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [], 100, 20, 0, "modelo-que-ninguem-precificou"));

        var devolvida = await Cliente().EnviarAsync(Requisicao(FinalidadeIa.Atendimento));

        devolvida.CustoUsd.Should().BeNull("sem preço o painel não pode mostrar grátis");
        _gravadas.Should().ContainSingle().Which.CustoUsd.Should().BeNull();
    }

    [Fact]
    public async Task FalhaAoGravarNaoAnulaOCustoDevolvidoAoTurno()
    {
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [], 1_000_000, 0, 0, ModeloComPreco));
        _registro.RegistrarAsync(Arg.Any<ChamadaIa>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("banco fora"));

        var devolvida = await Cliente().EnviarAsync(Requisicao(FinalidadeIa.Atendimento));

        devolvida.CustoUsd.Should().Be(3m, "o custo é do turno mesmo quando a linha se perde");
    }

    [Fact]
    public async Task CustoQueALinhaRecusaPorTamanhoNaoDerrubaNemAnulaOCustoDoTurno()
    {
        // A linha recusa custo a partir de US$ 1.000.000 (numeric(12,6)); ela se perde em log, e o turno segue com o valor.
        var tabela = new PrecosModelosAgenteOptions();
        tabela.Modelos["modelo-caro"] = new PrecoModeloAgente(2_000_000_000m, 0m, 0m);
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [], 1_000_000, 0, 0, "modelo-caro"));
        var cliente = new RegistroChamadaIaLlmClient(_interno, _registro, TimeProvider.System,
            NullLogger<RegistroChamadaIaLlmClient>.Instance, Options.Create(tabela));

        var devolvida = await cliente.EnviarAsync(Requisicao(FinalidadeIa.Atendimento));

        _gravadas.Should().BeEmpty();
        devolvida.CustoUsd.Should().Be(2_000_000_000m);
    }

    [Fact]
    public async Task SemEmpresaNaoGravaMasDevolveOCusto()
    {
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [], 1_000_000, 0, 0, ModeloComPreco));

        var devolvida = await Cliente().EnviarAsync(Requisicao(empresa: Guid.Empty));

        _gravadas.Should().BeEmpty();
        devolvida.CustoUsd.Should().Be(3m);
    }

    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired, DesfechoChamadaIa.SemSaldo402)]
    [InlineData(HttpStatusCode.TooManyRequests, DesfechoChamadaIa.Limite429)]
    [InlineData(HttpStatusCode.InternalServerError, DesfechoChamadaIa.Erro)]
    public async Task FalhaHttpGravaODesfechoERelanca(HttpStatusCode status, DesfechoChamadaIa desfecho)
    {
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new HttpRequestException("falhou", null, status));

        var act = () => Cliente().EnviarAsync(Requisicao());

        await act.Should().ThrowAsync<HttpRequestException>();
        var c = _gravadas.Should().ContainSingle().Subject;
        c.Desfecho.Should().Be(desfecho);
        c.TokensEntrada.Should().Be(0);
    }

    [Fact]
    public async Task ChamadaDoTurnoGravaOIdDoTurnoNoSucessoENaFalha()
    {
        // #3287 (R5): o caminho do .env também liga a chamada ao turno.
        var turno = Guid.NewGuid();
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new RespostaLlm("end_turn", [new BlocoTextoLlm("ok")], 1, 1)),
                _ => Task.FromException<RespostaLlm>(new HttpRequestException("fora", null, HttpStatusCode.BadGateway)));

        await Cliente().EnviarAsync(Requisicao() with { TurnoAgenteId = turno });
        await FluentActions.Awaiting(() => Cliente().EnviarAsync(Requisicao() with { TurnoAgenteId = turno }))
            .Should().ThrowAsync<HttpRequestException>();

        _gravadas.Should().HaveCount(2).And.OnlyContain(c => c.TurnoAgenteId == turno);
    }

    [Fact]
    public async Task OutraExcecaoGravaErro()
    {
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new InvalidOperationException("cli caiu"));

        var act = () => Cliente().EnviarAsync(Requisicao(FinalidadeIa.Ficha));

        await act.Should().ThrowAsync<InvalidOperationException>();
        _gravadas.Should().ContainSingle().Which.Desfecho.Should().Be(DesfechoChamadaIa.Erro);
    }

    [Fact]
    public async Task CancelamentoNaoGrava()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new OperationCanceledException(cts.Token));

        var act = () => Cliente().EnviarAsync(Requisicao(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task TimeoutDoClienteComTokenDoChamadorVivoGravaErro()
    {
        // HttpClient.Timeout e o prazo do CLI lançam TaskCanceledException sem que quem chamou tenha cancelado.
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new TaskCanceledException("timeout"));

        var act = () => Cliente().EnviarAsync(Requisicao(), CancellationToken.None);

        await act.Should().ThrowAsync<TaskCanceledException>();
        _gravadas.Should().ContainSingle().Which.Desfecho.Should().Be(DesfechoChamadaIa.Erro);
    }

    [Fact]
    public async Task FalhaAoGravarNaoInterrompeAResposta()
    {
        var resposta = new RespostaLlm("end_turn", [], 1, 1);
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(resposta);
        _registro.RegistrarAsync(Arg.Any<ChamadaIa>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("banco fora"));

        var devolvida = await Cliente().EnviarAsync(Requisicao());

        devolvida.Should().BeSameAs(resposta);
    }

    [Fact]
    public async Task BancoLentoNaoSeguraARespostaAlemDoPrazoDoRegistro()
    {
        var resposta = new RespostaLlm("end_turn", [], 1, 1);
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(resposta);
        _registro.RegistrarAsync(Arg.Any<ChamadaIa>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.Delay(Timeout.Infinite, c.Arg<CancellationToken>()));

        var tarefa = new RegistroChamadaIaLlmClient(_interno, _registro, TimeProvider.System,
            NullLogger<RegistroChamadaIaLlmClient>.Instance, Precos(), TimeSpan.FromSeconds(1)).EnviarAsync(Requisicao());
        var venceu = await Task.WhenAny(tarefa, Task.Delay(TimeSpan.FromSeconds(10)));

        venceu.Should().BeSameAs(tarefa, "o registro tem prazo próprio e não prende a resposta da IA");
        (await tarefa).Should().BeSameAs(resposta);
    }

    [Fact]
    public async Task PrimeiraGravacaoLentaAcimaDeTresSegundosNaoPerdeALinha()
    {
        // #3083: provedor frio (modelo do EF, conexão, SET do tenant) passou de 3 s no CI e a linha sumiu.
        var concluidas = 0;
        var resposta = new RespostaLlm("end_turn", [], 1, 1);
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(resposta);
        _registro.RegistrarAsync(Arg.Any<ChamadaIa>(), Arg.Any<CancellationToken>())
            .Returns(async c => { await Task.Delay(TimeSpan.FromSeconds(3.5), c.Arg<CancellationToken>()); concluidas++; });

        await Cliente().EnviarAsync(Requisicao());

        concluidas.Should().Be(1, "a gravação lenta terminou dentro do prazo e a linha existe");
    }

    [Fact]
    public async Task LinhaPerdidaViraLogDeErroMedivel()
    {
        // #3083: perda não pode ser silenciosa; "RegistroChamadaIa perdido" em nível Error é o que se conta em produção.
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm("end_turn", [], 1, 1));
        _registro.RegistrarAsync(Arg.Any<ChamadaIa>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("banco fora"));
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<RegistroChamadaIaLlmClient>>();

        await new RegistroChamadaIaLlmClient(_interno, _registro, TimeProvider.System, logger, Precos()).EnviarAsync(Requisicao());

        logger.ReceivedCalls().Select(c => c.GetArguments()).Where(a => a.Length == 5)
            .Should().ContainSingle(a => (Microsoft.Extensions.Logging.LogLevel)a[0]! == Microsoft.Extensions.Logging.LogLevel.Error
                && a[2]!.ToString()!.StartsWith("RegistroChamadaIa perdido"));
    }

    [Fact]
    public async Task SemEmpresaNaoGravaMasResponde()
    {
        var resposta = new RespostaLlm("end_turn", [], 1, 1);
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(resposta);

        var devolvida = await Cliente().EnviarAsync(Requisicao(empresa: Guid.Empty));

        devolvida.Should().BeSameAs(resposta);
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public void DisponivelVemDoClienteInterno()
    {
        _interno.Disponivel.Returns(true); _interno.DisponivelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        Cliente().Disponivel.Should().BeTrue();
    }

    [Fact]
    public async Task OQueNaoSaiuNaoGravaComoErro()
    {
        // #3064 (A1): disjuntor aberto ou cliente desligado não são chamada; só log, sem inflar a contagem.
        _interno.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<Task<RespostaLlm>>(_ => throw new ProvedorIaIndisponivelException("disjuntor aberto"));

        await FluentActions.Awaiting(() => Cliente().EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();

        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task DisponivelAsyncEncaminhaAoInterno()
    {
        _interno.DisponivelAsync(Empresa, Arg.Any<CancellationToken>()).Returns(true);
        (await Cliente().DisponivelAsync(Empresa)).Should().BeTrue();
    }
}
