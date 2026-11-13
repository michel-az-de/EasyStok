using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Automacoes;

/// <summary>
/// #1610: lembrete do link de pagamento ~10 min antes de vencer, pelo caminho das automáticas. Relógio controlado: o
/// <c>CobrancaPedidoJob</c> roda a cada 60 s e chama o use case a cada rodada enquanto a cobrança estiver na antecedência.
/// Só avisa (decisão do Felipe para link vencido): nada é reemitido nem cancelado aqui.
/// </summary>
public class LembrarCobrancaVencendoUseCaseTests
{
    private static readonly DateTime Emitida = new(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Pedido _pedido;
    private readonly Conversa _conversa;
    private readonly List<CobrancaPedido> _cobrancas = [];
    private readonly Dictionary<string, IdempotencyKey> _chaves = [];
    private readonly ICobrancaPedidoRepository _cobrancaRepo = Substitute.For<ICobrancaPedidoRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IIdempotencyKeyRepository _idempotencia = Substitute.For<IIdempotencyKeyRepository>();
    private readonly IRegraAutomaticaRepository _regras = Substitute.For<IRegraAutomaticaRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly IConsentimentoContatoRepository _consentimentos = Substitute.For<IConsentimentoContatoRepository>();
    private readonly ICanalMensageria _whats = Substitute.For<ICanalMensageria>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(Emitida));

    public LembrarCobrancaVencendoUseCaseTests()
    {
        _pedido = Pedido.Criar(_empresaId, origem: "whatsapp");
        _pedido.Status = StatusPedidoMapper.AguardandoPagamento;
        _pedidos.GetByIdAsync(_empresaId, _pedido.Id).Returns(_pedido);

        _conversa = Conversa.Abrir(_empresaId, "5511988887777", Emitida.AddMinutes(-5));
        _conversa.RegistrarEntrada(Emitida.AddMinutes(-4));
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(_conversa);

        _cobrancaRepo.ListarDoPedidoAsync(_empresaId, _pedido.Id, Arg.Any<CancellationToken>()).Returns(_ => _cobrancas.ToList());
        _whats.Canal.Returns(CanalConversa.WhatsApp);
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.lembrete");

        // Tabela de idempotência em memória: o índice único de (chave, empresa, recurso) é o que segura o segundo aviso.
        _idempotencia.TryBeginAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var chave = $"{ci.ArgAt<Guid>(1)}|{ci.ArgAt<string>(2)}|{ci.ArgAt<string>(0)}";
                if (_chaves.TryGetValue(chave, out var existente)) return (existente, false);
                var nova = IdempotencyKey.CriarPendente(ci.ArgAt<string>(0), ci.ArgAt<Guid>(1), ci.ArgAt<string>(2), null, ci.ArgAt<TimeSpan>(4));
                _chaves[chave] = nova;
                return (nova, true);
            });
        _idempotencia.When(r => r.MarcarFalhouAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()))
            .Do(ci => _chaves.Values.Single(k => k.Id == ci.ArgAt<Guid>(0)).Status = StatusIdempotencyKey.Falhou);
        _idempotencia.When(r => r.MarcarConcluidoAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(ci => _chaves.Values.Single(k => k.Id == ci.ArgAt<Guid>(0)).Status = StatusIdempotencyKey.Concluido);
        _idempotencia.TryReabrirAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var chave = _chaves.Values.Single(k => k.Id == ci.ArgAt<Guid>(0));
                if (chave.Status != StatusIdempotencyKey.Falhou) return false;
                chave.Status = StatusIdempotencyKey.Pendente;
                return true;
            });
    }

    private LembrarCobrancaVencendoUseCase UseCase()
    {
        var variaveis = new VariaveisAtendimento(_clientes, _pedidos);
        var disparo = new DispararAutomacaoUseCase(_regras, _conversas, _clientes, Substitute.For<IExpedienteLojaRepository>(),
            variaveis, new PoliticaEnvioCliente(_consentimentos), new ResolvedorCanal([_whats]), new FakeUnitOfWork(), _relogio,
            NullLogger<DispararAutomacaoUseCase>.Instance);
        return new LembrarCobrancaVencendoUseCase(_tenant, _cobrancaRepo, _pedidos, _idempotencia, disparo, _relogio,
            NullLogger<LembrarCobrancaVencendoUseCase>.Instance);
    }

    /// <summary>Link de 30 min emitido em <see cref="Emitida"/>, mandado na conversa.</summary>
    private CobrancaPedido Link()
    {
        var c = CobrancaPedido.CriarOnline(_empresaId, _pedido.Id, 170m, $"pref-{Guid.NewGuid():N}", "https://mp.test/pix",
            Emitida.AddMinutes(30), 1, Emitida, _conversa.Id);
        _cobrancas.Add(c);
        return c;
    }

    private CobrancaPedidoVencendo Item(CobrancaPedido c) => new(c.Id, _empresaId, _pedido.Id);

    private void Minuto(int minuto) => _relogio.SetUtcNow(new DateTimeOffset(Emitida.AddMinutes(minuto)));

    private int Envios() => _whats.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ICanalMensageria.EnviarTextoAsync));

    [Fact]
    public async Task AvisaUmaVezSoNosUltimosDezMinutos()
    {
        var link = Link();

        Minuto(19);
        (await UseCase().ExecuteAsync(Item(link))).Situacao.Should().Be(SituacaoLembreteCobranca.Ignorada, "faltam 11 minutos");
        Envios().Should().Be(0);

        Minuto(21);
        var aviso = await UseCase().ExecuteAsync(Item(link));
        aviso.Situacao.Should().Be(SituacaoLembreteCobranca.Disparada);
        aviso.Automacao.Should().Be(ResultadoAutomacao.Enviada);
        var codigo = CodigoCurto.De(_pedido.Id);
        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno, Arg.Is<string>(t => t.Contains(codigo)), Arg.Any<CancellationToken>());
        _tenant.Received().SetCurrentTenant(_empresaId);

        // As rodadas seguintes do job (a cada 60 s) acham a mesma cobrança: não avisa duas vezes.
        for (var minuto = 22; minuto < 30; minuto++)
        {
            Minuto(minuto);
            (await UseCase().ExecuteAsync(Item(link))).Situacao.Should().Be(SituacaoLembreteCobranca.JaAvisada);
        }
        Envios().Should().Be(1);
        await _cobrancaRepo.DidNotReceive().AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>());
        link.Status.Should().Be(Domain.Enums.Pagamentos.StatusCobrancaPedido.Pendente, "só avisa: não reemite nem cancela");
    }

    [Fact]
    public async Task PagamentoAntesCancelaOLembrete()
    {
        var link = Link();
        link.MarcarPaga("pay-1", 170m, "pix", Emitida.AddMinutes(12));
        _pedido.Status = StatusPedidoMapper.Aguardando;

        Minuto(21);
        (await UseCase().ExecuteAsync(Item(link))).Situacao.Should().Be(SituacaoLembreteCobranca.Ignorada);

        Envios().Should().Be(0);
        _chaves.Should().BeEmpty("nada foi reservado para um link pago");
    }

    [Fact]
    public async Task BaixaManualComLinkAindaPendenteNaoAvisa()
    {
        // O pedido foi pago no balcão e o link continua pendente no Mercado Pago (QuitacaoManualLinkPendente).
        var link = Link();
        _pedido.Status = StatusPedidoMapper.Aguardando;

        Minuto(21);
        (await UseCase().ExecuteAsync(Item(link))).Situacao.Should().Be(SituacaoLembreteCobranca.Ignorada);
        Envios().Should().Be(0);
    }

    [Fact]
    public async Task PedidoCanceladoNaoAvisa()
    {
        var link = Link();
        _pedido.Status = StatusPedidoMapper.Cancelado;

        Minuto(21);
        (await UseCase().ExecuteAsync(Item(link))).Situacao.Should().Be(SituacaoLembreteCobranca.Ignorada);
        Envios().Should().Be(0);
    }

    [Fact]
    public async Task LinkSubstituidoNaoAvisaEONovoTemOProprio()
    {
        var antigo = Link();
        antigo.Cancelar("troca_forma", Emitida.AddMinutes(5));

        Minuto(21);
        (await UseCase().ExecuteAsync(Item(antigo))).Situacao.Should().Be(SituacaoLembreteCobranca.Ignorada, "o link foi substituído");
        Envios().Should().Be(0);
    }

    [Fact]
    public async Task AutomaticaDesligadaNaoEnvia()
    {
        _regras.ObterPorGatilhoAsync(_empresaId, GatilhoAutomacao.CobrancaVencendo, Arg.Any<CancellationToken>())
            .Returns(RegraAutomatica.Criar(_empresaId, GatilhoAutomacao.CobrancaVencendo, "Vence já, {pedido}.", ligada: false, Emitida));
        var link = Link();

        Minuto(21);
        var r = await UseCase().ExecuteAsync(Item(link));

        r.Automacao.Should().Be(ResultadoAutomacao.Desligada);
        Envios().Should().Be(0);
    }

    [Fact]
    public async Task TextoEditadoPelaDonaVence()
    {
        _regras.ObterPorGatilhoAsync(_empresaId, GatilhoAutomacao.CobrancaVencendo, Arg.Any<CancellationToken>())
            .Returns(RegraAutomatica.Criar(_empresaId, GatilhoAutomacao.CobrancaVencendo, "Seu Pix do {pedido} vence já!", ligada: true, Emitida));
        var link = Link();

        Minuto(25);
        (await UseCase().ExecuteAsync(Item(link))).Automacao.Should().Be(ResultadoAutomacao.Enviada);

        await _whats.Received(1).EnviarTextoAsync(_conversa.ContatoIdExterno,
            $"Seu Pix do {CodigoCurto.De(_pedido.Id)} vence já!", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ForaDaJanelaDe24hNaoEnvia()
    {
        var link = Link();
        // Cliente escreveu pela última vez há mais de 24 h: texto livre não sai (só modelo aprovado).
        var velha = Conversa.Abrir(_empresaId, "5511977776666", Emitida.AddHours(-26));
        velha.RegistrarEntrada(Emitida.AddHours(-25));
        _conversas.ObterPorIdAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()).Returns(velha);

        Minuto(21);
        (await UseCase().ExecuteAsync(Item(link))).Automacao.Should().Be(ResultadoAutomacao.ForaDaJanela);
        Envios().Should().Be(0);
    }

    /// <summary>
    /// No máximo uma vez: o timeout do canal pode ter entregado, então a falha não volta na rodada seguinte. Exceção do
    /// disparo (banco fora, desligamento) também encerra a reserva, em vez de deixá-la presa como pendente.
    /// </summary>
    [Fact]
    public async Task FalhaDoCanalNaoReenvia()
    {
        var link = Link();
        _whats.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("Meta fora"), _ => "wamid.2");

        Minuto(21);
        // #2004: HttpRequestException sem status é falha incerta (a Meta pode ter recebido).
        (await UseCase().ExecuteAsync(Item(link))).Automacao.Should().Be(ResultadoAutomacao.EnvioIncerto);
        Minuto(22);
        (await UseCase().ExecuteAsync(Item(link))).Situacao.Should().Be(SituacaoLembreteCobranca.JaAvisada);

        Envios().Should().Be(1);
        _chaves.Values.Should().ContainSingle().Which.Status.Should().Be(StatusIdempotencyKey.Concluido);
    }

    [Fact]
    public async Task ExcecaoNoDisparoEncerraAReserva()
    {
        var link = Link();
        _regras.ObterPorGatilhoAsync(_empresaId, GatilhoAutomacao.CobrancaVencendo, Arg.Any<CancellationToken>())
            .Returns<RegraAutomatica?>(_ => throw new TimeoutException("banco fora"));

        Minuto(21);
        await FluentActions.Awaiting(() => UseCase().ExecuteAsync(Item(link))).Should().ThrowAsync<TimeoutException>();

        _chaves.Values.Should().ContainSingle().Which.Status.Should().Be(StatusIdempotencyKey.Concluido);
        Envios().Should().Be(0);
    }
}
