using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.Services.Storefront;
using EasyStock.Application.Tests.Helpers;
using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using DomainCliente = EasyStock.Domain.Entities.Cliente;
using DomainPedido = EasyStock.Domain.Entities.Pedido;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Checkout;

/// <summary>
/// Checkout sem login (#1254): o guest passa pelo mesmo núcleo do checkout logado e da conversa
/// (<see cref="CheckoutCoreService"/>, S10), reserva a vaga da janela e é cobrado pelo Mercado Pago
/// (<see cref="GerarCobrancaPedidoUseCase"/>, S11). Antes, o pedido ia para a aprovação da dona sem
/// vaga e sem cobrança. Também a conversão da campanha (#1226): o pedido de quem recebeu campanha nos
/// últimos 7 dias marca o destinatário como <c>Pediu</c>, pela mesma regra do pedido da conversa.
/// </summary>
public class IniciarCheckoutGuestUseCaseTests
{
    private const string Slug = "casa-da-baba";
    private const string CepValido = "01310100";
    private static readonly DateOnly DataEntrega = new(2026, 6, 2); // terça-feira
    private static readonly Guid JanelaId = Guid.NewGuid();
    private static readonly Guid CardapioItemId = Guid.NewGuid();

    private sealed class Fixture
    {
        public StorefrontEntity Storefront { get; }
        public IStorefrontRepository StorefrontRepo { get; } = Substitute.For<IStorefrontRepository>();
        public ICardapioItemRepository CardapioRepo { get; } = Substitute.For<ICardapioItemRepository>();
        public IJanelaEntregaRepository JanelaRepo { get; } = Substitute.For<IJanelaEntregaRepository>();
        public IBloqueioEntregaRepository BloqueioRepo { get; } = Substitute.For<IBloqueioEntregaRepository>();
        public IFreteZonaRepository FreteZonaRepo { get; } = Substitute.For<IFreteZonaRepository>();
        public IGeocodingClient Geocoding { get; } = Substitute.For<IGeocodingClient>();
        public IVagaOcupadaRepository VagaRepo { get; } = Substitute.For<IVagaOcupadaRepository>();
        public IPedidoStorefrontRepository PedidoRepo { get; } = Substitute.For<IPedidoStorefrontRepository>();
        public IExpedienteLojaRepository ExpedienteRepo { get; } = Substitute.For<IExpedienteLojaRepository>();
        public IClienteStorefrontRepository ClienteRepo { get; } = Substitute.For<IClienteStorefrontRepository>();
        public ICobrancaPedidoRepository CobrancaRepo { get; } = Substitute.For<ICobrancaPedidoRepository>();
        public IMercadoPagoClient MpClient { get; } = Substitute.For<IMercadoPagoClient>();
        public IUnitOfWork Uow { get; } = Substitute.For<IUnitOfWork>();
        public ICampanhaRepository CampanhaRepo { get; } = Substitute.For<ICampanhaRepository>();
        public ITenantContextAccessor Tenant { get; } = Substitute.For<ITenantContextAccessor>();
        public List<CobrancaPedido> Cobrancas { get; } = new();
        public List<DomainPedido> Pedidos { get; } = new();
        public List<DomainCliente> Clientes { get; } = new();

        public Fixture()
        {
            Storefront = StorefrontEntity.Criar(Guid.NewGuid(), Slug, "Casa da Babá", 0m);
            Storefront.Ativar();
            StorefrontRepo.GetBySlugAsync(Slug, Arg.Any<CancellationToken>()).Returns(Storefront);

            var produto = new EasyStock.Domain.Entities.Produto
            {
                Id = Guid.NewGuid(),
                Nome = "Brigadeiro",
                PrecoReferencia = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(10m),
            };
            var item = CardapioItem.CriarAPartirDeProduto(Storefront.Id, produto);
            item.TornarVisivel();
            item.Produto = produto;
            CardapioRepo.GetByIdAsync(Storefront.Id, CardapioItemId, Arg.Any<CancellationToken>()).Returns(item);

            var janela = JanelaEntrega.Criar(Storefront.Id, 2, new TimeOnly(9, 0), new TimeOnly(12, 0), 5, "Manhã 9-12h");
            typeof(JanelaEntrega).GetProperty("Id")!.SetValue(janela, JanelaId);
            JanelaRepo.GetByIdAsync(JanelaId, Arg.Any<CancellationToken>()).Returns(janela);
            JanelaRepo.GetAtivasDoStorefrontAsync(Storefront.Id, Arg.Any<CancellationToken>())
                .Returns(new List<JanelaEntrega> { janela });
            BloqueioRepo.GetByStorefrontPeriodoAsync(Storefront.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(new List<BloqueioEntrega>());

            var zona = FreteZona.CriarPorCep(storefrontId: Storefront.Id, label: "SP Centro", valor: 5m,
                tempoEstimadoMinutos: 60, cepInicio: "01000000", cepFim: "01999999");
            FreteZonaRepo.GetAtivasDoStorefrontOrdenadasAsync(Storefront.Id, Arg.Any<CancellationToken>())
                .Returns(new List<FreteZona> { zona });
            FreteZonaRepo.BuscarZonaPelasAtivas();

            VagaRepo.OcuparAsync(JanelaId, DataEntrega, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => VagaOcupada.Ocupar(JanelaId, DataEntrega, ci.ArgAt<Guid>(2)));
            VagaRepo.ContarPorJanelaPeriodoAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<(Guid, DateOnly), int>());

            PedidoRepo.When(r => r.AddAsync(Arg.Any<DomainPedido>(), Arg.Any<CancellationToken>()))
                .Do(ci => Pedidos.Add(ci.Arg<DomainPedido>()));
            ClienteRepo.GetByTelefoneHashAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((DomainCliente?)null);
            ClienteRepo.When(r => r.AddAsync(Arg.Any<DomainCliente>(), Arg.Any<CancellationToken>()))
                .Do(ci => Clientes.Add(ci.Arg<DomainCliente>()));

            CobrancaRepo.ListarDoPedidoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(new List<CobrancaPedido>());
            CobrancaRepo.When(r => r.AddAsync(Arg.Any<CobrancaPedido>(), Arg.Any<CancellationToken>()))
                .Do(ci => Cobrancas.Add(ci.Arg<CobrancaPedido>()));
            MpClient.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>())
                .Returns(new PreferenceCriadaResult("pref-123", "https://mp.com/checkout/pref-123"));
        }

        public IniciarCheckoutGuestUseCase UseCase()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Acompanhamento:JwtSecret"] = new string('s', 40),
                })
                .Build();
            var frete = new CalcularFreteUseCase(StorefrontRepo, FreteZonaRepo, Substitute.For<ICepLookupClient>(),
                Geocoding, Substitute.For<IRotaClient>(), NullLogger<CalcularFreteUseCase>.Instance);
            var core = new CheckoutCoreService(StorefrontRepo, CardapioRepo, JanelaRepo, BloqueioRepo, frete,
                VagaRepo, PedidoRepo, ExpedienteRepo, NullLogger<CheckoutCoreService>.Instance, TimeProvider.System);
            var cobranca = new GerarCobrancaPedidoUseCase(Substitute.For<IPedidoRepository>(), StorefrontRepo, CobrancaRepo,
                MpClient, Uow, TimeProvider.System, NullLogger<GerarCobrancaPedidoUseCase>.Instance);
            return new IniciarCheckoutGuestUseCase(StorefrontRepo, core, cobranca, ClienteRepo, PedidoRepo, Uow,
                new AcompanhamentoTokenService(config, TimeProvider.System),
                new AtribuicaoPedidoCampanha(CampanhaRepo, TimeProvider.System), Tenant, TimeProvider.System,
                NullLogger<IniciarCheckoutGuestUseCase>.Instance);
        }
    }

    private static IniciarCheckoutGuestInput Input(string cep = CepValido) => new(
        Slug: Slug,
        Nome: "Maria Silva",
        Telefone: "(11) 98765-4321",
        Cep: cep,
        Numero: "120",
        Items: new List<CheckoutItemInput> { new(CardapioItemId, 2) },
        JanelaId: JanelaId,
        DataEntrega: DataEntrega,
        Observacoes: "sem açúcar");

    [Fact]
    public async Task ReservaJanelaEGeraCobranca()
    {
        var f = new Fixture();

        var r = await f.UseCase().ExecuteAsync(Input());

        await f.VagaRepo.Received(1).OcuparAsync(JanelaId, DataEntrega, r.PedidoId, Arg.Any<CancellationToken>());
        var cobranca = f.Cobrancas.Should().ContainSingle().Subject;
        cobranca.PedidoId.Should().Be(r.PedidoId);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.Valor.Should().Be(25m); // 2 x R$ 10 + frete R$ 5
        r.LinkPagamento.Should().Be("https://mp.com/checkout/pref-123");
        r.ExpiresIn.Should().Be(1800);
        r.FreteEstimado.Should().Be(5m);
        r.AcompanhamentoToken.Should().NotBeNullOrWhiteSpace();
        r.NumeroCurto.Should().Be(r.PedidoId.ToString("N")[..8].ToUpperInvariant());
    }

    [Fact]
    public async Task PedidoFicaAguardandoPagamento()
    {
        var f = new Fixture();

        var r = await f.UseCase().ExecuteAsync(Input());

        var pedido = f.Pedidos.Should().ContainSingle().Subject;
        pedido.Id.Should().Be(r.PedidoId);
        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        pedido.Origem.Should().Be("storefront-guest");
        pedido.ClienteNome.Should().Be("Maria Silva");
        pedido.ClienteTelefone.Should().Be("+5511987654321");
        pedido.ClienteId.Should().Be(f.Clientes.Should().ContainSingle().Subject.Id);
        pedido.Observacoes.Should().Contain("sem açúcar").And.Contain("[Guest] CEP 01310-100, numero 120");
    }

    [Fact]
    public async Task SemVagaLancaJanelaSemVagasSemCobrar()
    {
        var f = new Fixture();
        f.VagaRepo.OcuparAsync(JanelaId, DataEntrega, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new JanelaSemVagasException("lotada"));

        var act = () => f.UseCase().ExecuteAsync(Input());

        await act.Should().ThrowAsync<JanelaSemVagasException>();
        await f.MpClient.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
        f.Cobrancas.Should().BeEmpty();
    }

    [Fact]
    public async Task CepForaDaAreaRecusa()
    {
        // Mudança de comportamento: sem zona não há frete para cobrar; antes o guest aceitava e a dona negociava.
        var f = new Fixture();

        var act = () => f.UseCase().ExecuteAsync(Input(cep: "20040002"));

        await act.Should().ThrowAsync<CepSemCoberturaException>();
        await f.MpClient.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
    }

    [Fact]
    public async Task TelefoneInvalidoRecusaAntesDeTudo()
    {
        var f = new Fixture();

        var act = () => f.UseCase().ExecuteAsync(Input() with { Telefone = "123" });

        await act.Should().ThrowAsync<TelefoneInvalidoException>();
        await f.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
    }

    [Fact]
    public async Task PedidoGuestDeQuemRecebeuCampanhaMarcaPediu()
    {
        var f = new Fixture();
        var empresaId = f.Storefront.EmpresaId;
        const string telefone = "+5511987654321";
        var cliente = new DomainCliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria", Telefone = telefone };
        f.ClienteRepo.GetByTelefoneHashAsync(empresaId, ClienteOtp.CalcularTelefoneHash(telefone), Arg.Any<CancellationToken>())
            .Returns(cliente);

        var agora = DateTime.UtcNow;
        var campanha = Campanha.Criar(empresaId, Guid.NewGuid(),
            new DadosCampanha("Bolo de fubá", "Oi {{nome}}", null, null, FiltroCampanha.ParaTodos, [], null, false, null),
            agora.AddDays(-3));
        var destinatario = CampanhaDestinatario.Criar(campanha, cliente.Id);
        destinatario.Enfileirar(1, Guid.NewGuid());
        destinatario.MarcarEnviado(agora.AddDays(-2));
        f.CampanhaRepo.ObterEnviadoParaAtribuirAsync(empresaId, cliente.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(destinatario);

        var resultado = await f.UseCase().ExecuteAsync(Input());

        destinatario.Status.Should().Be(StatusCampanhaDestinatario.Pediu);
        destinatario.PedidoId.Should().Be(resultado.PedidoId);
        f.Tenant.Received().SetCurrentTenant(empresaId);
    }

    [Fact]
    public async Task SemCampanhaRecenteOPedidoSegueNormal()
    {
        // Cliente novo (nenhum envio de campanha): o repositório não acha destinatário e nada muda.
        var f = new Fixture();

        var resultado = await f.UseCase().ExecuteAsync(Input());

        resultado.PedidoId.Should().NotBeEmpty();
        f.Pedidos.Should().ContainSingle(p => p.Id == resultado.PedidoId);
        f.Cobrancas.Should().ContainSingle();
    }

    [Fact]
    public async Task ClienteBloqueadoNaoFechaPedidoNemOcupaVaga()
    {
        // #1291: o bloqueio vale em todos os canais (spec 05-crm-e-pos-venda); antes só a conversa conferia.
        var f = new Fixture();
        var bloqueado = DomainCliente.CriarParaStorefront(f.Storefront.EmpresaId, "hash", TimeProvider.System);
        bloqueado.Bloquear("calote", DateTime.UtcNow);
        f.ClienteRepo.GetByTelefoneHashAsync(f.Storefront.EmpresaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(bloqueado);

        var act = () => f.UseCase().ExecuteAsync(Input());

        await act.Should().ThrowAsync<EasyStock.Domain.Exceptions.ClienteBloqueadoException>();
        f.Pedidos.Should().BeEmpty();
        await f.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
        await f.MpClient.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
    }

    [Fact]
    public async Task NumeroDoEnderecoVaiParaOGeocodeDoFretePorRaio()
    {
        // #1291: o checkout cota o frete como o POST /frete/calcular, com o número no geocode.
        var f = new Fixture();
        f.Storefront.ConfigurarFreteRaio(0, 0, 1.4, 500, 5000,
            """[{"id":"ate-5km","ateMetros":5000,"valorCentavos":2500}]""");
        f.Geocoding.GeocodificarAsync(Arg.Any<GeocodeQuery>(), Arg.Any<CancellationToken>())
            .Returns(new GeocodeResultado(0, 0.025, Confiavel: true));

        var r = await f.UseCase().ExecuteAsync(Input());

        await f.Geocoding.Received().GeocodificarAsync(
            Arg.Is<GeocodeQuery>(q => q.Numero == "120" && q.Cep == CepValido), Arg.Any<CancellationToken>());
        r.FreteEstimado.Should().Be(25m);
    }

    // ── Ponte #1306: o site ainda não manda janela; sem ela, o guest segue o modo antigo ──

    [Fact]
    public async Task SemJanela_ModoAntigoVaiParaAprovacaoSemVagaNemCobranca()
    {
        var f = new Fixture();

        var r = await f.UseCase().ExecuteAsync(Input() with { JanelaId = null, DataEntrega = null });

        var pedido = f.Pedidos.Should().ContainSingle().Subject;
        pedido.Id.Should().Be(r.PedidoId);
        pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba);
        pedido.Origem.Should().Be("storefront-guest");
        pedido.ClienteNome.Should().Be("Maria Silva");
        pedido.ClienteTelefone.Should().Be("+5511987654321");
        pedido.Observacoes.Should().Contain("[Guest] CEP 01310-100, numero 120");
        r.LinkPagamento.Should().BeNull();
        r.ExpiresIn.Should().Be(0);
        r.AcompanhamentoToken.Should().NotBeNullOrWhiteSpace();
        await f.VagaRepo.DidNotReceiveWithAnyArgs().OcuparAsync(default, default, default, default);
        await f.MpClient.DidNotReceiveWithAnyArgs().CriarPreferenceAsync(default!, default);
        f.Cobrancas.Should().BeEmpty();
        await f.Uow.Received().CommitAsync();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SoJanelaOuSoDataRecusa(bool comJanela, bool comData)
    {
        var f = new Fixture();
        var input = Input() with
        {
            JanelaId = comJanela ? JanelaId : null,
            DataEntrega = comData ? DataEntrega : null,
        };

        var act = () => f.UseCase().ExecuteAsync(input);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>().WithMessage("*janela*data*");
        f.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task SemJanela_ClienteBloqueadoRecusa()
    {
        var f = new Fixture();
        var bloqueado = DomainCliente.CriarParaStorefront(f.Storefront.EmpresaId, "hash", TimeProvider.System);
        bloqueado.Bloquear("calote", DateTime.UtcNow);
        f.ClienteRepo.GetByTelefoneHashAsync(f.Storefront.EmpresaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(bloqueado);

        var act = () => f.UseCase().ExecuteAsync(Input() with { JanelaId = null, DataEntrega = null });

        await act.Should().ThrowAsync<EasyStock.Domain.Exceptions.ClienteBloqueadoException>();
        f.Pedidos.Should().BeEmpty();
    }
}
