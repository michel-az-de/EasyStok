using System.Net;
using NSubstitute.ExceptionExtensions;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Integration.Crypto;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Services.Ia;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Domain.Entities.Ia;
using EasyStock.Domain.Enums.Ia;
using EasyStock.Domain.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.Services.Ia;

/// <summary>#3064 (S2b de #2948): roteador por loja atrás de flag, cota suave em US$ e custo pela faixa horária.</summary>
public class RoteadorIaLlmClientTests
{
    private const string ChaveSecreta = "valor-de-teste-da-loja";
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Provedor = Guid.NewGuid();

    // Segunda, 11h UTC: fora do pico.
    private static readonly DateTimeOffset ForaDoPico = new(2026, 11, 23, 11, 0, 0, TimeSpan.Zero);

    private readonly IAgenteLlmClient _doEnv = Substitute.For<IAgenteLlmClient>();
    private readonly ITenantFeatureFlagRepository _flags = Substitute.For<ITenantFeatureFlagRepository>();
    private readonly ITenantAtual _tenant = Substitute.For<ITenantAtual>();
    private readonly IRotasIa _rotas = Substitute.For<IRotasIa>();
    private readonly IIntegrationCredentialResolver _cofre = Substitute.For<IIntegrationCredentialResolver>();
    private readonly IClienteProvedorIa _provedores = Substitute.For<IClienteProvedorIa>();
    private readonly IRegistroChamadaIa _registro = Substitute.For<IRegistroChamadaIa>();
    private readonly List<ChamadaIa> _gravadas = [];
    private readonly LogEmLista _log = new();
    private readonly FakeTimeProvider _relogio = new(ForaDoPico);

    public RoteadorIaLlmClientTests()
    {
        _registro.RegistrarAsync(Arg.Do<ChamadaIa>(_gravadas.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _tenant.EmpresaId.Returns(Empresa);
        _flags.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>()).Returns([FeatureCatalogo.IaProvedorPorLoja]);
        _cofre.ObterAsync<ChaveProvedorIa>(Empresa, $"ia:{Provedor}", AmbienteIntegracao.Production, Arg.Any<CancellationToken>())
            .Returns(new ChaveProvedorIa(ChaveSecreta));
        _rotas.ObterAsync(Empresa, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Rota(orcamento: 10m));
        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(0m);
        _provedores.EnviarAsync(Arg.Any<DestinoIa>(), Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm("end_turn", [new BlocoTextoLlm("ok")], 1_000_000, 100_000, 0, "deepseek-flash"));
    }

    private static RotaIa Rota(decimal? orcamento, bool comPreco = true, bool caiuNoPadrao = false, int? saidaMax = null, int? prazoSegundos = null)
    {
        var agora = ForaDoPico.UtcDateTime;
        var modelo = ModeloIa.Criar(Empresa, Provedor, "deepseek-flash", "Flash",
            comPreco ? 0.30m : null, comPreco ? 1.20m : null, 0.006m, 0.5m, agora);
        modelo.DefinirSaidaMax(saidaMax, agora);
        return new RotaIa(Provedor, "https://api.deepseek.com/anthropic", AutenticacaoProvedorIa.XApiKey, orcamento, modelo, caiuNoPadrao, prazoSegundos);
    }

    private RoteadorIaLlmClient Roteador() =>
        new(_doEnv, _flags, _tenant, _rotas, _cofre, _provedores, _registro, _relogio, _log);

    private static RequisicaoLlm Requisicao(Guid? empresa = null, string? modelo = null) =>
        new("s", [new MensagemLlm(MensagemLlm.Usuario, [new BlocoTextoLlm("oi")])], [], modelo)
        {
            EmpresaId = empresa ?? Empresa,
            Finalidade = FinalidadeIa.Atendimento,
        };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SemFlagDelegaAoEnvSemTocarRotaCofreNemProvedor(bool envDisponivel)
    {
        _flags.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>()).Returns([]);
        _doEnv.Disponivel.Returns(envDisponivel);
        _doEnv.DisponivelAsync(Empresa, Arg.Any<CancellationToken>()).Returns(envDisponivel);
        var esperada = new RespostaLlm("end_turn", [], 1, 2);
        var requisicao = Requisicao();
        _doEnv.EnviarAsync(requisicao, Arg.Any<CancellationToken>()).Returns(esperada);
        var roteador = Roteador();

        (await roteador.DisponivelAsync(Empresa)).Should().Be(envDisponivel);
        roteador.Disponivel.Should().Be(envDisponivel);
        (await roteador.EnviarAsync(requisicao)).Should().BeSameAs(esperada);

        await _doEnv.Received(1).EnviarAsync(requisicao, Arg.Any<CancellationToken>());
        await _rotas.DidNotReceiveWithAnyArgs().ObterAsync(default, default, default);
        await _cofre.DidNotReceiveWithAnyArgs().ObterAsync<ChaveProvedorIa>(default, default!, default, default);
        await _provedores.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default!, default);
        _gravadas.Should().BeEmpty("sem flag quem grava é o registro da S2a, dentro do cliente do .env");
        await _flags.Received(1).ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>()); // memo por escopo
    }

    [Fact]
    public async Task DestinoLevaOTetoDeSaidaDoModeloEOPrazoDoProvedor()
    {
        _rotas.ObterAsync(Empresa, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Rota(orcamento: 10m, saidaMax: 8192, prazoSegundos: 90));

        await Roteador().EnviarAsync(Requisicao());

        await _provedores.Received(1).EnviarAsync(
            Arg.Is<DestinoIa>(d => d.SaidaMax == 8192 && d.PrazoSegundos == 90), Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemTetoNemPrazoProprioODestinoVaiComNulosEOClienteUsaOGlobal()
    {
        await Roteador().EnviarAsync(Requisicao());

        await _provedores.Received(1).EnviarAsync(
            Arg.Is<DestinoIa>(d => d.SaidaMax == null && d.PrazoSegundos == null), Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaAoLerAFlagSegueNoEnvSemDerrubarOTurno()
    {
        _flags.ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<string>>>(_ => throw new TimeoutException("banco lento"));
        _doEnv.DisponivelAsync(Empresa, Arg.Any<CancellationToken>()).Returns(true);

        (await Roteador().DisponivelAsync(Empresa)).Should().BeTrue();
        await Roteador().EnviarAsync(Requisicao());

        await _doEnv.Received(1).EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
        await _provedores.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default!, default);
        _log.Erros.Should().Contain(e => e.Contains("falha ao ler a flag"));
    }

    [Fact]
    public async Task ComFlagFalhaDeBancoOuCofreFicaIndisponivelSemLancar()
    {
        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task<decimal>>(_ => throw new TimeoutException("banco lento"));
        (await Roteador().DisponivelAsync(Empresa)).Should().BeFalse();

        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(0m);
        _cofre.ObterAsync<ChaveProvedorIa>(Empresa, Arg.Any<string>(), Arg.Any<AmbienteIntegracao>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChaveProvedorIa?>>(_ => throw new ChaveMestraAusenteException("KEK ausente"));
        (await Roteador().DisponivelAsync(Empresa)).Should().BeFalse();
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task SemEmpresaDelegaAoEnvSemLerFlag()
    {
        await Roteador().EnviarAsync(Requisicao(Guid.Empty));

        await _doEnv.Received(1).EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
        await _flags.DidNotReceiveWithAnyArgs().ListarAtivasAsync(default, default);
    }

    [Fact]
    public async Task ComFlagChamaOProvedorDaLojaEGravaUmaLinhaComCustoForaDoPico()
    {
        var resposta = await Roteador().EnviarAsync(Requisicao(modelo: "claude-sonnet-5"));

        resposta.Modelo.Should().Be("deepseek-flash");
        await _provedores.Received(1).EnviarAsync(
            Arg.Is<DestinoIa>(d => d.ProvedorId == Provedor && d.Chave == ChaveSecreta && d.Modelo == "deepseek-flash"
                && d.Autenticacao == AutenticacaoProvedorIa.XApiKey && d.BaseUrl == "https://api.deepseek.com/anthropic"),
            Arg.Is<RequisicaoLlm>(r => r.Modelo == "deepseek-flash"), Arg.Any<CancellationToken>());
        await _doEnv.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        var c = _gravadas.Should().ContainSingle().Subject;
        c.ProvedorId.Should().Be(Provedor);
        c.ModeloId.Should().NotBeNull();
        c.Desfecho.Should().Be(DesfechoChamadaIa.Ok);
        c.Finalidade.Should().Be(FinalidadeIa.Atendimento);
        // 1M entrada * 0,30 + 100k saída * 1,20 / 1M = 0,42; fora do pico * 0,5 = 0,21.
        c.CustoUsd.Should().Be(0.21m);
    }

    [Fact]
    public async Task NoPicoOCustoEhCheio()
    {
        _relogio.SetUtcNow(new DateTimeOffset(2026, 11, 24, 7, 0, 0, TimeSpan.Zero)); // terça, 07h UTC: pico

        await Roteador().EnviarAsync(Requisicao());

        _gravadas.Should().ContainSingle().Which.CustoUsd.Should().Be(0.42m);
    }

    [Theory]
    [InlineData(HttpStatusCode.PaymentRequired, DesfechoChamadaIa.SemSaldo402)]
    [InlineData(HttpStatusCode.TooManyRequests, DesfechoChamadaIa.Limite429)]
    [InlineData(HttpStatusCode.InternalServerError, DesfechoChamadaIa.Erro)]
    public async Task FalhaHttpDoProvedorGravaODesfechoERelanca(HttpStatusCode status, DesfechoChamadaIa desfecho)
    {
        _provedores.EnviarAsync(Arg.Any<DestinoIa>(), Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("falhou", null, status));

        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao())).Should().ThrowAsync<HttpRequestException>();

        var c = _gravadas.Should().ContainSingle().Subject;
        c.Desfecho.Should().Be(desfecho);
        c.CustoUsd.Should().BeNull();
        c.ProvedorId.Should().Be(Provedor);
    }

    [Fact]
    public async Task DisjuntorAbertoNaoGrava()
    {
        _provedores.EnviarAsync(Arg.Any<DestinoIa>(), Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ProvedorIaIndisponivelException("disjuntor"));

        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelamentoDeQuemChamouNaoGrava()
    {
        using var cts = new CancellationTokenSource();
        _provedores.EnviarAsync(Arg.Any<DestinoIa>(), Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<Task<RespostaLlm>>(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao(), cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        _gravadas.Should().BeEmpty();
    }

    // Prova com orçamento de US$ 10 (aceite): abaixo, aviso ao chegar a 80% e estouro.
    [Fact]
    public async Task OrcamentoDe10DolaresAbaixoNaoAvisa()
    {
        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(7.99m);

        (await Roteador().DisponivelAsync(Empresa)).Should().BeTrue();
        await Roteador().EnviarAsync(Requisicao());

        _log.Avisos.Should().NotContain(a => a.Contains("Cota de IA"));
        _gravadas.Should().ContainSingle().Which.Desfecho.Should().Be(DesfechoChamadaIa.Ok);
    }

    [Fact]
    public async Task OrcamentoDe10DolaresAvisaAoChegarA80PorCentoESegueChamando()
    {
        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(8m);

        (await Roteador().DisponivelAsync(Empresa)).Should().BeTrue();
        await Roteador().EnviarAsync(Requisicao());

        _log.Avisos.Should().Contain(a => a.Contains("Cota de IA perto do fim"));
        _gravadas.Should().ContainSingle().Which.Desfecho.Should().Be(DesfechoChamadaIa.Ok);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("10.5")]
    public async Task OrcamentoDe10DolaresEstouradoFicaIndisponivelEGravaCotaEstouradaUmaVezPorEscopo(string gasto)
    {
        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(decimal.Parse(gasto, System.Globalization.CultureInfo.InvariantCulture));
        var roteador = Roteador();

        // Como os chamadores reais: checam o portão e, com false, a equipe atende sem chamar EnviarAsync.
        (await roteador.DisponivelAsync(Empresa)).Should().BeFalse();
        (await roteador.DisponivelAsync(Empresa)).Should().BeFalse();
        var c = _gravadas.Should().ContainSingle("o portão grava cota_estourada uma vez por escopo").Subject;
        c.Desfecho.Should().Be(DesfechoChamadaIa.CotaEstourada);
        (c.TokensEntrada, c.TokensSaida, c.CustoUsd).Should().Be((0, 0, (decimal?)null));
        c.ProvedorId.Should().Be(Provedor);

        // Corrida entre o portão e o envio, no mesmo escopo: não sai e não duplica a linha.
        await FluentActions.Awaiting(() => roteador.EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();
        _gravadas.Should().ContainSingle();

        // Outro escopo (outra requisição) que só envia também grava.
        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();
        _gravadas.Should().HaveCount(2).And.OnlyContain(g => g.Desfecho == DesfechoChamadaIa.CotaEstourada);
        await _provedores.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default!, default);
    }

    [Fact]
    public async Task ComFlagFalhaDeBancoOuCofreNoEnvioViraIndisponivelSemGravar()
    {
        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task<decimal>>(_ => throw new TimeoutException("banco lento"));
        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>().WithInnerException(typeof(TimeoutException));

        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(0m);
        _cofre.ObterAsync<ChaveProvedorIa>(Empresa, Arg.Any<string>(), Arg.Any<AmbienteIntegracao>(), Arg.Any<CancellationToken>())
            .Returns<Task<ChaveProvedorIa?>>(_ => throw new ChaveMestraAusenteException("KEK ausente"));
        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();

        _gravadas.Should().BeEmpty();
        await _provedores.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default!, default);
    }

    [Fact]
    public async Task ChamadaEmVooQuandoACotaEstouraTerminaEGravaOCusto()
    {
        // Cota suave (D6): a checagem é antes de sair; a chamada que já saiu termina e grava, mesmo passando do teto.
        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(9.9m);

        await Roteador().EnviarAsync(Requisicao());

        _gravadas.Should().ContainSingle().Which.CustoUsd.Should().Be(0.21m);
    }

    [Fact]
    public async Task ModeloSemPrecoComOrcamentoFicaIndisponivelComLogProprio()
    {
        _rotas.ObterAsync(Empresa, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Rota(orcamento: 10m, comPreco: false));

        (await Roteador().DisponivelAsync(Empresa)).Should().BeFalse();
        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();

        _log.Erros.Should().Contain(e => e.Contains("IA sem preço"));
        _log.Avisos.Should().NotContain(a => a.Contains("Cota de IA estourada"));
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task ModeloSemPrecoSemOrcamentoRodaComCustoNulo()
    {
        _rotas.ObterAsync(Empresa, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Rota(orcamento: null, comPreco: false));

        await Roteador().EnviarAsync(Requisicao());

        _gravadas.Should().ContainSingle().Which.CustoUsd.Should().BeNull();
        await _rotas.DidNotReceiveWithAnyArgs().GastoDoMesAsync(default, default, default, default);
    }

    [Fact]
    public async Task SemRotaOuSemChaveFicaIndisponivelSemGravar()
    {
        _rotas.ObterAsync(Empresa, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((RotaIa?)null);
        (await Roteador().DisponivelAsync(Empresa)).Should().BeFalse();

        _rotas.ObterAsync(Empresa, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Rota(10m));
        _cofre.ObterAsync<ChaveProvedorIa>(Empresa, Arg.Any<string>(), Arg.Any<AmbienteIntegracao>(), Arg.Any<CancellationToken>())
            .Returns((ChaveProvedorIa?)null);
        (await Roteador().DisponivelAsync(Empresa)).Should().BeFalse();
        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();

        _gravadas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TenantDiferenteOuVazioRecusaAntesDeLerRotaOuChave(bool vazio)
    {
        _tenant.EmpresaId.Returns(vazio ? Guid.Empty : Guid.NewGuid());

        (await Roteador().DisponivelAsync(Empresa)).Should().BeFalse();
        await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao()))
            .Should().ThrowAsync<ProvedorIaIndisponivelException>();

        await _rotas.DidNotReceiveWithAnyArgs().ObterAsync(default, default, default);
        await _cofre.DidNotReceiveWithAnyArgs().ObterAsync<ChaveProvedorIa>(default, default!, default, default);
        _gravadas.Should().BeEmpty();
    }

    [Fact]
    public async Task ModeloPedidoSemCadastroCaiNoPadraoComLog()
    {
        _rotas.ObterAsync(Empresa, "claude-velho", Arg.Any<CancellationToken>()).Returns(Rota(10m, caiuNoPadrao: true));

        await Roteador().EnviarAsync(Requisicao(modelo: "claude-velho"));

        _log.Infos.Should().Contain(i => i.Contains("claude-velho") && i.Contains("padrão"));
    }

    [Fact]
    public async Task FlagERotaSaoLidasUmaVezPorEscopo()
    {
        var roteador = Roteador();
        for (var i = 0; i < 3; i++)
        {
            await roteador.DisponivelAsync(Empresa);
            await roteador.EnviarAsync(Requisicao());
        }

        await _flags.Received(1).ListarAtivasAsync(Empresa, Arg.Any<CancellationToken>());
        await _rotas.Received(1).ObterAsync(Empresa, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GastoDoMesESomadoUmaVezPorEscopo()
    {
        // #3287 (R10): até 7 somas num turno de 6 voltas; o gasto lido no começo vale pelo escopo (cota suave, D6).
        var roteador = Roteador();
        for (var i = 0; i < 3; i++)
        {
            await roteador.DisponivelAsync(Empresa);
            await roteador.EnviarAsync(Requisicao());
        }

        await _rotas.Received(1).GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscopoNovoLeOGastoDeNovo()
    {
        await Roteador().EnviarAsync(Requisicao());
        await Roteador().EnviarAsync(Requisicao());

        await _rotas.Received(2).GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaAoSomarOGastoNaoFicaGuardadaNoMemo()
    {
        _rotas.GastoDoMesAsync(Empresa, Provedor, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<decimal>(new TimeoutException("banco lento")), Task.FromResult(0m));
        var roteador = Roteador();

        (await roteador.DisponivelAsync(Empresa)).Should().BeFalse();
        (await roteador.DisponivelAsync(Empresa)).Should().BeTrue();
    }

    [Fact]
    public async Task ChamadaDoTurnoGravaOIdDoTurno()
    {
        var turno = Guid.NewGuid();

        await Roteador().EnviarAsync(Requisicao() with { TurnoAgenteId = turno });

        _gravadas.Should().ContainSingle().Which.TurnoAgenteId.Should().Be(turno);
    }

    [Fact]
    public async Task AChaveNaoApareceEmLogExcecaoNemChamadaIa()
    {
        _provedores.EnviarAsync(Arg.Any<DestinoIa>(), Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("falhou", null, HttpStatusCode.Unauthorized));

        var ex = await FluentActions.Awaiting(() => Roteador().EnviarAsync(Requisicao())).Should().ThrowAsync<HttpRequestException>();

        ex.Which.ToString().Should().NotContain(ChaveSecreta);
        _log.Todas.Should().NotContain(l => l.Contains(ChaveSecreta));
        new DestinoIa(Provedor, "https://x", AutenticacaoProvedorIa.Bearer, ChaveSecreta, "m").ToString().Should().NotContain(ChaveSecreta);
        new ChaveProvedorIa(ChaveSecreta).ToString().Should().NotContain(ChaveSecreta);
        System.Text.Json.JsonSerializer.Serialize(_gravadas.Single()).Should().NotContain(ChaveSecreta);
    }

    private sealed class LogEmLista : ILogger<RoteadorIaLlmClient>
    {
        private readonly List<(LogLevel Nivel, string Texto)> _linhas = [];
        public IEnumerable<string> Todas => _linhas.Select(l => l.Texto);
        public IEnumerable<string> Erros => _linhas.Where(l => l.Nivel == LogLevel.Error).Select(l => l.Texto);
        public IEnumerable<string> Avisos => _linhas.Where(l => l.Nivel == LogLevel.Warning).Select(l => l.Texto);
        public IEnumerable<string> Infos => _linhas.Where(l => l.Nivel == LogLevel.Information).Select(l => l.Texto);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _linhas.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " " + exception)));
    }
}
