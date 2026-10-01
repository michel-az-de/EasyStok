using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute.ExceptionExtensions;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.Services.Storefront;

/// <summary>
/// Núcleo do checkout compartilhado (S10): fases 1 e 2 do ADR-0014 num caminho só, usado pelo site e
/// pelo atendimento. Os testes do site continuam em <c>IniciarCheckoutUseCaseTests</c>.
/// </summary>
public class CheckoutCoreServiceTests
{
    private static readonly DateOnly DataEntrega = new(2026, 6, 2); // terça-feira
    private static readonly Guid JanelaId = Guid.NewGuid();
    private static readonly Guid CardapioItemId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private const string Cep = "01310-100";

    internal sealed class Cenario
    {
        public IStorefrontRepository StorefrontRepo { get; } = Substitute.For<IStorefrontRepository>();
        public ICardapioItemRepository CardapioRepo { get; } = Substitute.For<ICardapioItemRepository>();
        public IJanelaEntregaRepository JanelaRepo { get; } = Substitute.For<IJanelaEntregaRepository>();
        public IBloqueioEntregaRepository BloqueioRepo { get; } = Substitute.For<IBloqueioEntregaRepository>();
        public IFreteZonaRepository FreteZonaRepo { get; } = Substitute.For<IFreteZonaRepository>();
        public IVagaOcupadaRepository VagaRepo { get; } = Substitute.For<IVagaOcupadaRepository>();
        public IPedidoStorefrontRepository PedidoRepo { get; } = Substitute.For<IPedidoStorefrontRepository>();
        public IExpedienteLojaRepository ExpedienteRepo { get; } = Substitute.For<IExpedienteLojaRepository>();

        /// <summary>Configuração padrão do atendimento (preparo 60 + respiro 40), para quem cria pelo atendimento.</summary>
        public EasyStock.Application.Ports.Output.Persistence.IConfiguracaoAtendimentoRepository ConfiguracaoAtendimentoRepo { get; } =
            Substitute.For<EasyStock.Application.Ports.Output.Persistence.IConfiguracaoAtendimentoRepository>();

        /// <summary>Véspera da entrega, 12:00 em Brasília: qualquer janela do dia seguinte atende o prazo.</summary>
        public FakeTimeProvider Relogio { get; } = new(new DateTimeOffset(2026, 6, 1, 15, 0, 0, TimeSpan.Zero));
        public StorefrontEntity Storefront { get; }
        public CardapioItem CardapioItem { get; }
        public Guid JanelaId => CheckoutCoreServiceTests.JanelaId;
        public Guid CardapioItemId => CheckoutCoreServiceTests.CardapioItemId;
        public DateOnly DataEntrega => CheckoutCoreServiceTests.DataEntrega;
        public List<Pedido> PedidosAdicionados { get; } = new();
        public List<PedidoItem> ItensAdicionados { get; } = new();

        /// <summary>Campanhas do cliente (S30): sem envio recente, o pedido não é atribuído a nenhuma.</summary>
        public EasyStock.Application.Ports.Output.Persistence.Campanhas.ICampanhaRepository CampanhaRepo { get; } =
            Substitute.For<EasyStock.Application.Ports.Output.Persistence.Campanhas.ICampanhaRepository>();

        public EasyStock.Application.Services.Campanhas.AtribuicaoPedidoCampanha Atribuicao() => new(CampanhaRepo, Relogio);

        public Cenario()
        {
            Storefront = StorefrontEntity.Criar(Guid.NewGuid(), "casa-da-baba", "Casa da Babá", 0m);
            Storefront.Ativar();
            ConfiguracaoAtendimentoRepo.GetOrDefaultAsync(Storefront.EmpresaId)
                .Returns(EasyStock.Domain.Entities.Atendimento.ConfiguracaoAtendimento.CriarPadrao(Storefront.EmpresaId));
            StorefrontRepo.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(Storefront);
            StorefrontRepo.GetByEmpresaAsync(Storefront.EmpresaId, Arg.Any<CancellationToken>()).Returns(Storefront);

            var produto = new Produto
            {
                Id = Guid.NewGuid(),
                Nome = "Brigadeiro",
                PrecoReferencia = EasyStock.Domain.ValueObjects.Dinheiro.FromDecimal(10m),
            };
            var cardapioItem = CardapioItem.CriarAPartirDeProduto(Storefront.Id, produto);
            cardapioItem.TornarVisivel();
            cardapioItem.Produto = produto;
            CardapioItem = cardapioItem;
            typeof(CardapioItem).GetProperty("Id")!.SetValue(cardapioItem, CheckoutCoreServiceTests.CardapioItemId);
            CardapioRepo.GetByIdAsync(Storefront.Id, CheckoutCoreServiceTests.CardapioItemId, Arg.Any<CancellationToken>())
                .Returns(cardapioItem);

            var janela = JanelaEntrega.Criar(Storefront.Id, 2, new TimeOnly(9, 0), new TimeOnly(12, 0), 5, "Manhã 9-12h");
            typeof(JanelaEntrega).GetProperty("Id")!.SetValue(janela, CheckoutCoreServiceTests.JanelaId);
            JanelaRepo.GetByIdAsync(CheckoutCoreServiceTests.JanelaId, Arg.Any<CancellationToken>()).Returns(janela);
            JanelaRepo.GetAtivasDoStorefrontAsync(Storefront.Id, Arg.Any<CancellationToken>())
                .Returns(new List<JanelaEntrega> { janela });

            BloqueioRepo.GetByStorefrontPeriodoAsync(
                    Storefront.Id, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(new List<BloqueioEntrega>());

            var zona = FreteZona.CriarPorCep(
                storefrontId: Storefront.Id,
                label: "SP Centro",
                valor: 5m,
                tempoEstimadoMinutos: 60,
                cepInicio: "01000000",
                cepFim: "01999999");
            FreteZonaRepo.GetAtivasDoStorefrontOrdenadasAsync(Storefront.Id, Arg.Any<CancellationToken>())
                .Returns(new List<FreteZona> { zona });

            VagaRepo.OcuparAsync(CheckoutCoreServiceTests.JanelaId, CheckoutCoreServiceTests.DataEntrega,
                    Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(ci => VagaOcupada.Ocupar(CheckoutCoreServiceTests.JanelaId, CheckoutCoreServiceTests.DataEntrega, ci.ArgAt<Guid>(2)));
            VagaRepo.ContarPorJanelaPeriodoAsync(
                    Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns(new Dictionary<(Guid, DateOnly), int>());

            PedidoRepo.When(r => r.AddAsync(Arg.Any<Pedido>(), Arg.Any<CancellationToken>()))
                .Do(ci => PedidosAdicionados.Add(ci.Arg<Pedido>()));
            PedidoRepo.When(r => r.AddItemAsync(Arg.Any<PedidoItem>(), Arg.Any<CancellationToken>()))
                .Do(ci => ItensAdicionados.Add(ci.Arg<PedidoItem>()));
        }

        public CheckoutCoreService Servico() => new(
            StorefrontRepo, CardapioRepo, JanelaRepo, BloqueioRepo, FreteZonaRepo, VagaRepo, PedidoRepo,
            ExpedienteRepo, NullLogger<CheckoutCoreService>.Instance, Relogio);
    }

    private static CheckoutCoreInput Input(Cenario c) => new(
        ClienteId: ClienteId,
        Itens: new List<ItemPedidoCheckout> { new(CardapioItemId, 2, "sem granulado") },
        JanelaId: JanelaId,
        DataEntrega: DataEntrega,
        Cep: Cep,
        Origem: "storefront",
        Slug: "casa-da-baba");

    [Fact]
    public async Task CriaPedidoEReservaVaga()
    {
        var c = new Cenario();

        var reservado = await c.Servico().CriarPedidoComReservaAsync(Input(c));

        reservado.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        reservado.Pedido.Origem.Should().Be("storefront");
        reservado.Pedido.ClienteId.Should().Be(ClienteId);
        reservado.Pedido.Total.Valor.Should().Be(25m); // 2 x 10 + frete 5
        reservado.Total.Should().Be(25m);
        reservado.Storefront.Should().BeSameAs(c.Storefront);

        reservado.Itens.Should().ContainSingle();
        var item = reservado.Itens[0];
        item.CardapioItemId.Should().Be(CardapioItemId);
        item.Observacao.Should().Be("sem granulado");
        item.Quantidade.Should().Be(2);
        item.PrecoUnitario.Should().Be(10m);
        reservado.ItemFrete.Subtotal.Should().Be(5m);
        c.ItensAdicionados.Should().HaveCount(2);

        await c.VagaRepo.Received(1).OcuparAsync(JanelaId, DataEntrega, reservado.Pedido.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JanelaLotadaCancelaRascunho()
    {
        var c = new Cenario();
        c.VagaRepo.OcuparAsync(JanelaId, DataEntrega, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new JanelaSemVagasException("lotada"));

        var act = () => c.Servico().CriarPedidoComReservaAsync(Input(c));

        await act.Should().ThrowAsync<JanelaSemVagasException>();
        var pedido = c.PedidosAdicionados.Should().ContainSingle().Subject;
        pedido.Status.Should().Be(StatusPedidoMapper.Cancelado);
        pedido.CanceladoEm.Should().NotBeNull();
    }

    [Fact]
    public async Task CopiaLinhaDoCardapioParaOItem()
    {
        var c = new Cenario();

        var reservado = await c.Servico().CriarPedidoComReservaAsync(Input(c));

        reservado.Itens[0].LinhaSnapshot.Should().Be("paraServir");
        reservado.ItemFrete.LinhaSnapshot.Should().BeNull("frete não é produto");
        // S53: a conservação vai junto (o item de teste é o padrão, ambiente); frete não tem.
        reservado.Itens[0].ConservacaoSnapshot.Should().Be("ambiente");
        reservado.ItemFrete.ConservacaoSnapshot.Should().BeNull("frete não é produto");
    }
    // ── S16: antecedência mínima ─────────────────────────────────────────

    [Fact]
    public async Task RejeitaJanelaAbaixoDoPrazo()
    {
        // Janela 9-12h do dia da entrega; agora 08:00 em Brasília; prazo 60 + 40 = 100 min → 09:40 > 09:00.
        var c = new Cenario();
        c.Relogio.Advance(new DateTimeOffset(2026, 6, 2, 11, 0, 0, TimeSpan.Zero) - c.Relogio.GetUtcNow());

        var act = () => c.Servico().CriarPedidoComReservaAsync(
            Input(c) with { Prazo = new PrazoPreparoCheckout(TempoPreparoPadraoMinutos: 60, RespiroMinutos: 40) });

        (await act.Should().ThrowAsync<RegraDeDominioVioladaException>())
            .WithMessage("janela_abaixo_do_prazo");
        c.PedidosAdicionados.Should().BeEmpty();
        await c.VagaRepo.DidNotReceive().OcuparAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AceitaJanelaNoPrazo()
    {
        // Agora 07:00 em Brasília; 100 min → 08:40, antes das 09:00.
        var c = new Cenario();
        c.Relogio.Advance(new DateTimeOffset(2026, 6, 2, 10, 0, 0, TimeSpan.Zero) - c.Relogio.GetUtcNow());

        var reservado = await c.Servico().CriarPedidoComReservaAsync(
            Input(c) with { Prazo = new PrazoPreparoCheckout(TempoPreparoPadraoMinutos: 60, RespiroMinutos: 40) });

        reservado.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task ItemEsgotadoRecusaOCheckout()
    {
        var c = new Cenario();
        c.CardapioItem.MarcarEsgotado();

        var act = () => c.Servico().CriarPedidoComReservaAsync(Input(c));

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>()
            .WithMessage($"*{CardapioItemId}*indisponível*");
        c.PedidosAdicionados.Should().BeEmpty("item esgotado não pode virar pedido");
        await c.VagaRepo.DidNotReceive().OcuparAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
