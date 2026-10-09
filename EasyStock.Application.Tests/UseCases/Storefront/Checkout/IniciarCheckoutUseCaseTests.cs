using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Checkout;

/// <summary>
/// Testes do <see cref="IniciarCheckoutUseCase"/> (TASK-EZ-CHECKOUT-001).
///
/// <para>Cobertura:</para>
/// <list type="bullet">
///   <item>Happy path — retorna pedidoId + initPointUrl.</item>
///   <item>Sem cookie (401) — tratado no controller; use case recebe clienteId válido.</item>
///   <item>Carrinho vazio → RegraDeDominioVioladaException.</item>
///   <item>Item invisível → RegraDeDominioVioladaException.</item>
///   <item>CEP inválido → CepInvalidoException.</item>
///   <item>CEP sem cobertura → CepSemCoberturaException.</item>
///   <item>Janela inativa/inválida → RegraDeDominioVioladaException.</item>
///   <item>Janela dia errado → RegraDeDominioVioladaException.</item>
///   <item>Janela esgotada (Fase 2) → JanelaSemVagasException, pedido cancelado.</item>
///   <item>MP timeout (Fase 3) → MercadoPagoIndisponivelException.</item>
///   <item>Storefront inexistente → StorefrontNaoEncontradoException.</item>
/// </list>
/// </summary>
public class IniciarCheckoutUseCaseTests
{
    private const string SlugValido = "casa-da-baba";
    private static readonly Guid ClienteId = Guid.NewGuid();

    // Data fixa (qualquer terça-feira) para testes determinísticos
    private static readonly DateOnly DataEntrega = new(2026, 6, 2); // terça-feira (DayOfWeek=2)
    private static readonly Guid JanelaId = Guid.NewGuid();
    private static readonly Guid CardapioItemId1 = Guid.NewGuid();
    private const string CepValido = "01310100";

    // ── Fixture ────────────────────────────────────────────────────────────

    private sealed record Fakes(
        IStorefrontRepository StorefrontRepo,
        ICardapioItemRepository CardapioRepo,
        IJanelaEntregaRepository JanelaRepo,
        IBloqueioEntregaRepository BloqueioRepo,
        IFreteZonaRepository FreteZonaRepo,
        IVagaOcupadaRepository VagaRepo,
        IPedidoStorefrontRepository PedidoRepo,
        CheckoutIdempotencyService IdempotencyService,
        IMercadoPagoClient MpClient,
        IExpedienteLojaRepository ExpedienteRepo,
        StorefrontEntity Storefront,
        CardapioItem CardapioItem1,
        JanelaEntrega Janela,
        FreteZona FreteZona)
    {
        // S11: a fase 3 grava a CobrancaPedido pelo GerarCobrancaPedidoUseCase.
        public ICobrancaPedidoRepository CobrancaRepo { get; init; } = CobrancaRepoVazio();
        public IUnitOfWork Uow { get; init; } = Substitute.For<IUnitOfWork>();

        /// <summary>Cliente da sessão (#1291): sem cadastro devolvido, não está bloqueado.</summary>
        public IClienteStorefrontRepository ClienteRepo { get; } = Substitute.For<IClienteStorefrontRepository>();

        /// <summary>Campanhas do cliente (#1226): sem envio recente, o pedido não é atribuído a nenhuma.</summary>
        public ICampanhaRepository CampanhaRepo { get; } = Substitute.For<ICampanhaRepository>();
        public ITenantContextAccessor Tenant { get; } = Substitute.For<ITenantContextAccessor>();
    }

    private static ICobrancaPedidoRepository CobrancaRepoVazio()
    {
        var repo = Substitute.For<ICobrancaPedidoRepository>();
        repo.ListarDoPedidoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CobrancaPedido>());
        return repo;
    }

    private static Fakes BuildFakes(bool storefrontAtivo = true)
    {
        var storefront = StorefrontEntity.Criar(
            empresaId: Guid.NewGuid(),
            slug: SlugValido,
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 0m);
        if (storefrontAtivo) storefront.Ativar();

        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>()).Returns(storefront);

        // Cardápio item com produto stub
        var produto = new EasyStock.Domain.Entities.Produto
        {
            Id = Guid.NewGuid(),
            Nome = "Brigadeiro",
            PrecoReferencia = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(10m),
        };
        var cardapioItem = CardapioItem.CriarAPartirDeProduto(storefront.Id, produto);
        cardapioItem.TornarVisivel();
        cardapioItem.Produto = produto; // nav p/ PrecoEfetivo() herdar PrecoReferencia (R$10)

        var cardapioRepo = Substitute.For<ICardapioItemRepository>();
        cardapioRepo.GetByIdAsync(storefront.Id, CardapioItemId1, Arg.Any<CancellationToken>())
            .Returns(cardapioItem);

        // Janela na terça-feira (DayOfWeek=2)
        var janela = JanelaEntrega.Criar(
            storefrontId: storefront.Id,
            diaDaSemana: 2,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: 5,
            label: "Manhã 9-12h");

        // Forçar o Id da janela para o valor esperado via reflection
        typeof(JanelaEntrega)
            .GetProperty("Id")!
            .SetValue(janela, JanelaId);

        var janelaRepo = Substitute.For<IJanelaEntregaRepository>();
        janelaRepo.GetByIdAsync(JanelaId, Arg.Any<CancellationToken>()).Returns(janela);
        janelaRepo.GetAtivasDoStorefrontAsync(storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<JanelaEntrega> { janela });

        var bloqueioRepo = Substitute.For<IBloqueioEntregaRepository>();
        bloqueioRepo.GetByStorefrontPeriodoAsync(
                storefront.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<BloqueioEntrega>());

        // FreteZona cobrindo CEP
        var freteZona = FreteZona.CriarPorCep(
            storefrontId: storefront.Id,
            label: "SP Centro",
            valor: 5m,
            tempoEstimadoMinutos: 60,
            cepInicio: "01000000",
            cepFim: "01999999");

        var freteZonaRepo = Substitute.For<IFreteZonaRepository>();
        freteZonaRepo.GetAtivasDoStorefrontOrdenadasAsync(storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new List<FreteZona> { freteZona });
        freteZonaRepo.BuscarZonaPelasAtivas();

        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        vagaRepo.OcuparAsync(JanelaId, DataEntrega, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => VagaOcupada.Ocupar(JanelaId, DataEntrega, ci.ArgAt<Guid>(2)));
        vagaRepo.ContarPorJanelaPeriodoAsync(
                Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), int>());

        var pedidoRepo = Substitute.For<IPedidoStorefrontRepository>();

        var idempotencyRepo = Substitute.For<ICheckoutIdempotencyRepository>();
        // InputValido() não tem ContentHash → service.TentarReservarAsync não é chamado.
        // Setups defensivos para cobrir testes que passam ContentHash explicitamente.
        idempotencyRepo.GetByKeyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<CheckoutIdempotency>());
        idempotencyRepo.TentarReservarAsync(
                Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var p = callInfo.Arg<CheckoutIdempotency>();
                return (true, p);
            });
        idempotencyRepo.GetByKeyHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<Guid>(0);
                var hash = callInfo.ArgAt<string>(1);
                return (CheckoutIdempotency?)CheckoutIdempotency.Criar(key, hash);
            });
        var idempotencyService = new CheckoutIdempotencyService(
            idempotencyRepo, NullLogger<CheckoutIdempotencyService>.Instance);

        var mpClient = Substitute.For<IMercadoPagoClient>();
        mpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new PreferenceCriadaResult("pref-123", "https://mp.com/checkout/pref-123"));

        // Sem registro de expediente = padrão automático (S40): não bloqueia o checkout.
        var expedienteRepo = Substitute.For<IExpedienteLojaRepository>();

        return new Fakes(storefrontRepo, cardapioRepo, janelaRepo, bloqueioRepo,
            freteZonaRepo, vagaRepo, pedidoRepo, idempotencyService, mpClient, expedienteRepo,
            storefront, cardapioItem, janela, freteZona);
    }

    private static IniciarCheckoutUseCase BuildUseCase(Fakes f)
    {
        var core = new CheckoutCoreService(
            f.StorefrontRepo,
            f.CardapioRepo,
            f.JanelaRepo,
            f.BloqueioRepo,
            new CalcularFreteUseCase(
                f.StorefrontRepo, f.FreteZonaRepo, Substitute.For<ICepLookupClient>(), Substitute.For<IGeocodingClient>(),
                Substitute.For<IRotaClient>(), NullLogger<CalcularFreteUseCase>.Instance),
            f.VagaRepo,
            f.PedidoRepo,
            f.ExpedienteRepo,
            NullLogger<CheckoutCoreService>.Instance,
            TimeProvider.System);
        return new(
            core,
            f.StorefrontRepo,
            f.ClienteRepo,
            f.IdempotencyService,
            new GerarCobrancaPedidoUseCase(
                Substitute.For<IPedidoRepository>(),
                f.StorefrontRepo,
                f.CobrancaRepo,
                f.MpClient,
                core,
                f.Uow,
                TimeProvider.System,
                NullLogger<GerarCobrancaPedidoUseCase>.Instance),
            new AtribuicaoPedidoCampanha(f.CampanhaRepo, TimeProvider.System),
            f.Tenant,
            f.Uow,
            NullLogger<IniciarCheckoutUseCase>.Instance);
    }

    private static IniciarCheckoutInput InputValido() => new(
        Slug: SlugValido,
        ClienteId: ClienteId,
        Items: new List<CheckoutItemInput> { new(CardapioItemId1, 2) },
        JanelaId: JanelaId,
        DataEntrega: DataEntrega,
        Cep: CepValido);

    // ── Testes ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_LojaFechadaNaMao_LancaLojaFechadaSemCriarPedido()
    {
        // S40: a pausa manual da dona recusa pedido novo do site; o horário não (checkout é agendado).
        var f = BuildFakes();
        var expediente = ExpedienteLoja.CriarPadrao(f.Storefront.EmpresaId);
        expediente.DefinirControle(EasyStock.Domain.Enums.Storefront.ControleManualLoja.ForcarFechada, null, DateTime.UtcNow);
        f.ExpedienteRepo.GetPublicoAsync(f.Storefront.EmpresaId, Arg.Any<CancellationToken>()).Returns(expediente);

        var act = () => BuildUseCase(f).ExecuteAsync(InputValido());

        await act.Should().ThrowAsync<LojaFechadaException>();
        await f.PedidoRepo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ForaDoHorarioSemPausaManual_NaoBloqueia()
    {
        var f = BuildFakes();
        var expediente = ExpedienteLoja.CriarPadrao(f.Storefront.EmpresaId);
        expediente.DefinirHorarios([]); // nenhum turno: fora do horário o tempo todo
        f.ExpedienteRepo.GetPublicoAsync(f.Storefront.EmpresaId, Arg.Any<CancellationToken>()).Returns(expediente);

        var resultado = await BuildUseCase(f).ExecuteAsync(InputValido());

        resultado.PedidoId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_RetornaPedidoIdEInitPointUrl()
    {
        var f = BuildFakes();
        var uc = BuildUseCase(f);

        var result = await uc.ExecuteAsync(InputValido());

        result.PedidoId.Should().NotBe(Guid.Empty);
        result.InitPointUrl.Should().Be("https://mp.com/checkout/pref-123");
        result.ExpiresIn.Should().Be(1800);

        await f.VagaRepo.Received(1).OcuparAsync(JanelaId, DataEntrega, result.PedidoId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistraCobrancaPedido()
    {
        // S11: o site passa pelo mesmo use case de cobrança da conversa e grava a CobrancaPedido.
        var f = BuildFakes();
        var gravadas = new List<CobrancaPedido>();
        f.CobrancaRepo.When(r => r.AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>()))
            .Do(ci => gravadas.Add(ci.Arg<CobrancaPedido>()));

        var result = await BuildUseCase(f).ExecuteAsync(InputValido());

        var cobranca = gravadas.Should().ContainSingle().Subject;
        cobranca.PedidoId.Should().Be(result.PedidoId);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.ReferenciaExterna.Should().Be("pref-123");
        cobranca.LinkPagamento.Should().Be(result.InitPointUrl);
        cobranca.Valor.Should().Be(25m);
        cobranca.ConversaId.Should().BeNull();
        await f.MpClient.Received(1).CriarPreferenceAsync(
            Arg.Is<CriarPreferenceCommand>(c => c.PedidoId == result.PedidoId && c.ExpiraEm != null),
            Arg.Any<CancellationToken>());
        await f.Uow.Received().CommitAsync();
    }

    [Fact]
    public async Task ExecuteAsync_HappyPath_PersistePedidoComTotalDeItensMaisFrete()
    {
        var f = BuildFakes();
        var uc = BuildUseCase(f);

        // 2 x Brigadeiro (R$10) + frete SP Centro (R$5) = R$25.
        // Regressão: fluxo logado não chamava RecalcularTotal e persistia Total=0.
        await uc.ExecuteAsync(InputValido());

        await f.PedidoRepo.Received().UpdateAsync(
            Arg.Is<EasyStock.Domain.Entities.Pedido>(p => p.Total.Valor == 25m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CarrinhoVazio_LancaExcecao()
    {
        var f = BuildFakes();
        var uc = BuildUseCase(f);

        var input = InputValido() with { Items = new List<CheckoutItemInput>() };

        await uc.Invoking(u => u.ExecuteAsync(input))
            .Should().ThrowAsync<RegraDeDominioVioladaException>()
            .WithMessage("*vazio*");
    }

    [Fact]
    public async Task ExecuteAsync_QtdZero_LancaExcecao()
    {
        var f = BuildFakes();
        var uc = BuildUseCase(f);

        var input = InputValido() with
        {
            Items = new List<CheckoutItemInput> { new(CardapioItemId1, 0) },
        };

        await uc.Invoking(u => u.ExecuteAsync(input))
            .Should().ThrowAsync<RegraDeDominioVioladaException>()
            .WithMessage("*Quantidade*");
    }

    [Fact]
    public async Task ExecuteAsync_ItemInvisivel_LancaExcecao()
    {
        var f = BuildFakes();
        // Item indisponível: retorna null
        f.CardapioRepo.GetByIdAsync(Arg.Any<Guid>(), CardapioItemId1, Arg.Any<CancellationToken>())
            .Returns((CardapioItem?)null);

        var uc = BuildUseCase(f);

        await uc.Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<RegraDeDominioVioladaException>()
            .WithMessage("*não está mais no cardápio*");
    }

    [Fact]
    public async Task ExecuteAsync_CepInvalido_LancaCepInvalidoException()
    {
        var f = BuildFakes();
        var uc = BuildUseCase(f);

        var input = InputValido() with { Cep = "abc" };

        await uc.Invoking(u => u.ExecuteAsync(input))
            .Should().ThrowAsync<CepInvalidoException>();
    }

    [Fact]
    public async Task ExecuteAsync_CepSemCobertura_LancaCepSemCoberturaException()
    {
        var f = BuildFakes();
        f.FreteZonaRepo.GetAtivasDoStorefrontOrdenadasAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<FreteZona>());

        var uc = BuildUseCase(f);

        await uc.Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<CepSemCoberturaException>();
    }

    [Fact]
    public async Task ExecuteAsync_StorefrontInexistente_LancaStorefrontNaoEncontradoException()
    {
        var f = BuildFakes();
        f.StorefrontRepo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var uc = BuildUseCase(f);

        await uc.Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task ExecuteAsync_JanelaInativa_LancaExcecao()
    {
        var f = BuildFakes();
        f.JanelaRepo.GetByIdAsync(JanelaId, Arg.Any<CancellationToken>())
            .Returns((JanelaEntrega?)null);

        var uc = BuildUseCase(f);

        await uc.Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<RegraDeDominioVioladaException>()
            .WithMessage("*não está mais disponível*");
    }

    [Fact]
    public async Task ExecuteAsync_JanelaDiaErrado_LancaExcecao()
    {
        var f = BuildFakes();

        // Criar janela de domingo (0), mas dataEntrega é terça (2)
        var janelaErrada = JanelaEntrega.Criar(
            storefrontId: f.Storefront.Id,
            diaDaSemana: 0,
            horaInicio: new TimeOnly(9, 0),
            horaFim: new TimeOnly(12, 0),
            capacidadeMaxima: 5,
            label: "Dom");

        typeof(JanelaEntrega).GetProperty("Id")!.SetValue(janelaErrada, JanelaId);

        f.JanelaRepo.GetByIdAsync(JanelaId, Arg.Any<CancellationToken>()).Returns(janelaErrada);

        var uc = BuildUseCase(f);

        await uc.Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<RegraDeDominioVioladaException>()
            .WithMessage("*dia*");
    }

    [Fact]
    public async Task ExecuteAsync_JanelaEsgotada_LancaJanelaSemVagasExceptionECancelaPedido()
    {
        var f = BuildFakes();
        f.VagaRepo.OcuparAsync(JanelaId, DataEntrega, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Throws(new JanelaSemVagasException("Esgotada."));

        var uc = BuildUseCase(f);

        await uc.Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<JanelaSemVagasException>();

        // Fase 1 deve ter sido cancelada (UpdateAsync chamado com status Cancelado)
        await f.PedidoRepo.Received().UpdateAsync(
            Arg.Is<EasyStock.Domain.Entities.Pedido>(p => p.Status == StatusPedidoMapper.Cancelado),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MpTimeout_LancaMercadoPagoIndisponivelException()
    {
        var f = BuildFakes();
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<OperationCanceledException>();

        var uc = BuildUseCase(f);

        await uc.Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<MercadoPagoIndisponivelException>();
    }

    public static TheoryData<Exception> FalhasDoMercadoPago => new()
    {
        new HttpRequestException("500"),
        new OperationCanceledException("timeout de 5 s"),
    };

    [Theory]
    [MemberData(nameof(FalhasDoMercadoPago))]
    public async Task ExecuteAsync_MercadoPagoFora_CancelaPedidoELiberaVaga(Exception falha)
    {
        // #1301: sem CobrancaPedido gravada o CobrancaPedidoJob não enxerga o pedido, e a vaga ficava presa.
        var f = BuildFakes();
        var pedidos = new List<EasyStock.Domain.Entities.Pedido>();
        f.PedidoRepo.When(r => r.AddAsync(Arg.Any<EasyStock.Domain.Entities.Pedido>(), Arg.Any<CancellationToken>()))
            .Do(ci => pedidos.Add(ci.Arg<EasyStock.Domain.Entities.Pedido>()));
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(falha);

        await BuildUseCase(f).Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<MercadoPagoIndisponivelException>("o site segue respondendo 503");

        var pedido = pedidos.Should().ContainSingle().Subject;
        pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        await f.VagaRepo.Received(1).LiberarPorPedidoAsync(
            pedido.Id, Arg.Is<string>(m => m.Contains("mercado_pago_indisponivel")), Arg.Any<CancellationToken>());
        await f.PedidoRepo.Received(1).AddEventoAsync(
            Arg.Is<EasyStock.Domain.Entities.PedidoEvento>(e =>
                e.PedidoId == pedido.Id && e.Tipo == "cancelado" && e.Detalhes == "mercado_pago_indisponivel"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PedidoDoSiteMarcaDestinatarioDaCampanhaComoPediu()
    {
        // #1226: a mesma regra do pedido da conversa (S30): quem recebeu campanha na semana e pede pelo
        // site conta como conversão. A requisição é anônima: o tenant da loja liga o filtro e a RLS.
        var f = BuildFakes();
        var destinatario = DestinatarioEnviado(f.Storefront.EmpresaId, ClienteId);
        f.CampanhaRepo.ObterEnviadoParaAtribuirAsync(
                f.Storefront.EmpresaId, ClienteId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(destinatario);

        var result = await BuildUseCase(f).ExecuteAsync(InputValido());

        destinatario.Status.Should().Be(StatusCampanhaDestinatario.Pediu);
        destinatario.PedidoId.Should().Be(result.PedidoId);
        f.Tenant.Received().SetCurrentTenant(f.Storefront.EmpresaId);
    }

    [Fact]
    public async Task AtribuicaoDaCampanhaNaoDependeDoMercadoPago()
    {
        // #1226: o pedido já existe (AguardandoPagamento) quando a cobrança falha; a conversão é gravada antes.
        var f = BuildFakes();
        var destinatario = DestinatarioEnviado(f.Storefront.EmpresaId, ClienteId);
        f.CampanhaRepo.ObterEnviadoParaAtribuirAsync(
                f.Storefront.EmpresaId, ClienteId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(destinatario);
        f.MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync<OperationCanceledException>();

        await BuildUseCase(f).Invoking(u => u.ExecuteAsync(InputValido()))
            .Should().ThrowAsync<MercadoPagoIndisponivelException>();

        destinatario.Status.Should().Be(StatusCampanhaDestinatario.Pediu);
        await f.Uow.Received(1).CommitAsync();
    }

    private static CampanhaDestinatario DestinatarioEnviado(Guid empresaId, Guid clienteId)
    {
        var agora = DateTime.UtcNow;
        var campanha = Campanha.Criar(empresaId, Guid.NewGuid(),
            new DadosCampanha("Bolo de fubá", "Oi {{nome}}", null, null, FiltroCampanha.ParaTodos, [], null, false, null),
            agora.AddDays(-3));
        var destinatario = CampanhaDestinatario.Criar(campanha, clienteId);
        destinatario.Enfileirar(1, Guid.NewGuid());
        destinatario.MarcarEnviado(agora.AddDays(-2));
        return destinatario;
    }

    [Fact]
    public async Task ExecuteAsync_MesmaIdempotencyKey_DevolveOMesmoPedidoSemNovaReserva()
    {
        // S10: a idempotência continua antes do núcleo; o replay não cria pedido nem ocupa vaga.
        var registros = new List<CheckoutIdempotency>();
        var idempotencyRepo = Substitute.For<ICheckoutIdempotencyRepository>();
        idempotencyRepo.GetByKeyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => registros.Where(r => r.Key == ci.ArgAt<Guid>(0)).ToList());
        idempotencyRepo.TentarReservarAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var p = ci.Arg<CheckoutIdempotency>();
                registros.Add(p);
                return (true, p);
            });
        idempotencyRepo.GetByKeyHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => registros.FirstOrDefault(r => r.Confere(ci.ArgAt<Guid>(0), ci.ArgAt<string>(1))));
        var f = BuildFakes() with
        {
            IdempotencyService = new CheckoutIdempotencyService(
                idempotencyRepo, NullLogger<CheckoutIdempotencyService>.Instance),
        };
        var input = InputValido() with { IdempotencyKey = Guid.NewGuid() };
        input = input with { ContentHash = CheckoutContentHasher.ComputarHash(input) };

        var primeira = await BuildUseCase(f).ExecuteAsync(input);
        var segunda = await BuildUseCase(f).ExecuteAsync(input);

        segunda.PedidoId.Should().Be(primeira.PedidoId);
        segunda.InitPointUrl.Should().Be(primeira.InitPointUrl);
        await f.PedidoRepo.Received(1).AddAsync(Arg.Any<EasyStock.Domain.Entities.Pedido>(), Arg.Any<CancellationToken>());
        await f.VagaRepo.Received(1).OcuparAsync(JanelaId, DataEntrega, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ClienteBloqueado_RecusaSemPedidoNemVaga()
    {
        // #1291: o bloqueio vale em todos os canais (spec 05-crm-e-pos-venda); antes só a conversa conferia.
        var f = BuildFakes();
        var cliente = EasyStock.Domain.Entities.Cliente.CriarParaStorefront(f.Storefront.EmpresaId, "hash", TimeProvider.System);
        cliente.Bloquear("calote", DateTime.UtcNow);
        f.ClienteRepo.GetByIdAsync(ClienteId, Arg.Any<CancellationToken>()).Returns(cliente);

        var act = () => BuildUseCase(f).ExecuteAsync(InputValido());

        await act.Should().ThrowAsync<EasyStock.Domain.Exceptions.ClienteBloqueadoException>();
        await f.PedidoRepo.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await f.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_MesmaIdempotencyKey_RespostaSoValeDepoisDoCommit()
    {
        // #1291: o UpdateAsync do repositório não salva; sem commit depois do RegistrarResposta, o retry
        // não via a resposta e recriava pedido, vaga e link. O banco aqui só enxerga o que foi commitado.
        var banco = new BancoIdempotenciaFake();
        var f = BuildFakes() with
        {
            IdempotencyService = new CheckoutIdempotencyService(
                banco.Repositorio(), NullLogger<CheckoutIdempotencyService>.Instance),
        };
        f.Uow.When(u => u.CommitAsync()).Do(_ => banco.Commitar());
        var input = InputValido() with { IdempotencyKey = Guid.NewGuid() };
        input = input with { ContentHash = CheckoutContentHasher.ComputarHash(input) };

        var primeira = await BuildUseCase(f).ExecuteAsync(input);
        banco.FimDaRequisicao();
        var segunda = await BuildUseCase(f).ExecuteAsync(input);

        segunda.PedidoId.Should().Be(primeira.PedidoId);
        await f.PedidoRepo.Received(1).AddAsync(Arg.Any<EasyStock.Domain.Entities.Pedido>(), Arg.Any<CancellationToken>());
        await f.VagaRepo.Received(1).OcuparAsync(JanelaId, DataEntrega, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Tabela de idempotência com a semântica do EF: a reserva salva na hora (o repositório faz
    /// <c>SaveChanges</c>), o <c>UpdateAsync</c> só marca a entidade e a mudança chega ao banco no commit.
    /// Cada leitura devolve uma instância nova, como uma consulta faria.
    /// </summary>
    private sealed class BancoIdempotenciaFake
    {
        private readonly Dictionary<(Guid Key, string Hash), (Guid? FaturaId, string? InitPoint)> _linhas = new();
        private readonly List<CheckoutIdempotency> _marcadas = new();

        public void Commitar()
        {
            foreach (var r in _marcadas)
                _linhas[(r.Key, r.ContentHash)] = (r.FaturaId, r.InitPoint);
            _marcadas.Clear();
        }

        /// <summary>O DbContext da requisição morre: o que não foi commitado se perde.</summary>
        public void FimDaRequisicao() => _marcadas.Clear();

        public ICheckoutIdempotencyRepository Repositorio()
        {
            var repo = Substitute.For<ICheckoutIdempotencyRepository>();
            repo.GetByKeyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => _linhas.Keys.Where(k => k.Key == ci.ArgAt<Guid>(0)).Select(Ler).ToList());
            repo.GetByKeyHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => _linhas.Keys
                    .Where(k => k.Key == ci.ArgAt<Guid>(0) && k.Hash == ci.ArgAt<string>(1).Trim().ToLowerInvariant())
                    .Select(Ler)
                    .FirstOrDefault());
            repo.TentarReservarAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var p = ci.Arg<CheckoutIdempotency>();
                    _linhas[(p.Key, p.ContentHash)] = (null, null);
                    return (true, p);
                });
            repo.When(r => r.UpdateAsync(Arg.Any<CheckoutIdempotency>(), Arg.Any<CancellationToken>()))
                .Do(ci => _marcadas.Add(ci.Arg<CheckoutIdempotency>()));
            return repo;
        }

        private CheckoutIdempotency Ler((Guid Key, string Hash) chave)
        {
            var registro = CheckoutIdempotency.Criar(chave.Key, chave.Hash);
            var (faturaId, initPoint) = _linhas[chave];
            if (faturaId is { } id && initPoint is not null)
                registro.VincularFatura(id, initPoint);
            return registro;
        }
    }
}
