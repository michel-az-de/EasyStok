using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ListarCardapioAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ReordenarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>
/// #1241 (F11): itens do cardápio pelo console, na vitrine da empresa logada. Tirar é esconder
/// (nunca apagar) e é idempotente; a lista de fora traz só os escondidos.
/// </summary>
public class ItensDoCardapioComandaUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public ItensDoCardapioComandaUseCaseTests()
    {
        _vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(_vitrine);
        _storefronts.GetByIdAsync(_vitrine.Id).Returns(_vitrine);
    }

    private ItensDoCardapioComandaUseCase Sut()
    {
        var aviso = new AvisoItemComInteresse(_storefronts, Substitute.For<IInteresseItemRepository>(), Substitute.For<IOperacaoEventPublisher>());
        return new ItensDoCardapioComandaUseCase(
            _storefronts,
            new AdicionarCardapioItemAdminUseCase(_storefronts, _cardapio, Substitute.For<IProdutoRepository>(), _uow),
            new EditarCardapioItemAdminUseCase(_cardapio, _uow),
            new ToggleVisibilidadeCardapioItemAdminUseCase(_cardapio, _uow, aviso),
            new ListarCardapioAdminUseCase(_storefronts, _cardapio),
            new ReordenarCardapioItemAdminUseCase(_cardapio, _uow),
            _cardapio);
    }

    private CardapioItem Item(string nome, bool visivel)
    {
        var item = CardapioItem.CriarAvulso(_vitrine.Id, nome, 30m, "Massas");
        if (visivel) item.TornarVisivel(); else item.Ocultar();
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, item.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    [Fact]
    public async Task TirarDoCardapio_EsconderSemApagar()
    {
        var item = Item("Lasanha", visivel: true);

        var r = await Sut().DefinirVisivelAsync(EmpresaId, item.Id, false);

        r.Visivel.Should().BeFalse();
        await _cardapio.DidNotReceiveWithAnyArgs().RemoveAsync(default!);
    }

    [Fact]
    public async Task TirarDeNovo_NaoRepoe()
    {
        var item = Item("Lasanha", visivel: false);

        var r = await Sut().DefinirVisivelAsync(EmpresaId, item.Id, false);

        r.Visivel.Should().BeFalse("o clique repetido não pode repor o item");
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ListarFora_TrazSoOsEscondidos()
    {
        var fora = Item("Nhoque", visivel: false);
        var dentro = Item("Lasanha", visivel: true);
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id).Returns([fora, dentro]);

        var lista = await Sut().ListarForaAsync(EmpresaId);

        lista.Should().ContainSingle().Which.CardapioItemId.Should().Be(fora.Id);
    }

    [Fact]
    public async Task ListarGestao_TrazOcultosEDesligadosNaOrdem()
    {
        var oculto = Item("Nhoque", visivel: false);
        var desligado = Item("Lasanha", visivel: true);
        desligado.MarcarEsgotado();
        oculto.DefinirOrdem(2);
        desligado.DefinirOrdem(1);
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns([desligado, oculto]);

        var lista = await Sut().ListarGestaoAsync(EmpresaId);

        lista.Select(i => (i.Nome, i.Visivel, i.Disponivel)).Should().Equal(
            ("lasanha", true, false), ("nhoque", false, true));
        lista.Should().OnlyContain(i => !i.ControlaSaldo, "itens avulsos não controlam saldo");
    }

    [Fact]
    public async Task DefinirOrdem_GravaOValorEntreOsVizinhos()
    {
        var item = Item("Lasanha", visivel: true);

        var r = await Sut().DefinirOrdemAsync(EmpresaId, item.Id, 1.5);

        r.Ordem.Should().Be(1.5);
        await _uow.Received(1).CommitAsync();
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData("Torta", null)]
    public async Task Incluir_SemNomeOuPreco_400(string? nome, int? preco)
    {
        var act = () => Sut().IncluirAsync(EmpresaId, new DadosItemCardapio(nome, LinhaProduto.ParaServir, "500 g", preco, null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
