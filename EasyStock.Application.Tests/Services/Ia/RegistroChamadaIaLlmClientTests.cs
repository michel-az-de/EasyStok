using System.Net;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Services.Ia;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using Microsoft.Extensions.Logging.Abstractions;
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

    private RegistroChamadaIaLlmClient Cliente() =>
        new(_interno, _registro, new FakeTimeProvider(new DateTimeOffset(2026, 11, 21, 17, 0, 0, TimeSpan.Zero)),
            NullLogger<RegistroChamadaIaLlmClient>.Instance);

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

        devolvida.Should().BeSameAs(resposta);
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
            NullLogger<RegistroChamadaIaLlmClient>.Instance, TimeSpan.FromSeconds(1)).EnviarAsync(Requisicao());
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

        await new RegistroChamadaIaLlmClient(_interno, _registro, TimeProvider.System, logger).EnviarAsync(Requisicao());

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
