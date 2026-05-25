using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
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
        StorefrontEntity Storefront,
        CardapioItem CardapioItem1,
        JanelaEntrega Janela,
        FreteZona FreteZona);

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

        return new Fakes(storefrontRepo, cardapioRepo, janelaRepo, bloqueioRepo,
            freteZonaRepo, vagaRepo, pedidoRepo, idempotencyService, mpClient,
            storefront, cardapioItem, janela, freteZona);
    }

    private static IniciarCheckoutUseCase BuildUseCase(Fakes f) => new(
        f.StorefrontRepo,
        f.CardapioRepo,
        f.JanelaRepo,
        f.BloqueioRepo,
        f.FreteZonaRepo,
        f.VagaRepo,
        f.PedidoRepo,
        f.IdempotencyService,
        f.MpClient,
        NullLogger<IniciarCheckoutUseCase>.Instance);

    private static IniciarCheckoutInput InputValido() => new(
        Slug: SlugValido,
        ClienteId: ClienteId,
        Items: new List<CheckoutItemInput> { new(CardapioItemId1, 2) },
        JanelaId: JanelaId,
        DataEntrega: DataEntrega,
        Cep: CepValido);

    // ── Testes ─────────────────────────────────────────────────────────────

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
            .WithMessage("*não encontrado*");
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
            .WithMessage("*inválida*");
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
}
