using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.EditarCardapioItemAdmin;
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
            _cardapio,
            _uow);
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
    public async Task ListarFora_TrazSoOsArquivados_NaoOsOcultosDoSite()
    {
        // D-M1-07: tirar (arquivar) é diferente de ocultar do site.
        var arquivado = Item("Nhoque", visivel: true);
        arquivado.Arquivar(DateTime.UtcNow);
        var oculto = Item("Lasanha", visivel: false);
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id).Returns([arquivado, oculto]);

        var lista = await Sut().ListarForaAsync(EmpresaId);

        lista.Should().ContainSingle().Which.CardapioItemId.Should().Be(arquivado.Id);
    }

    [Fact]
    public async Task Obter_TrazAFichaParaEditar()
    {
        var item = Item("Lasanha", visivel: true);
        item.AtualizarMetadata(ingredientes: "massa, ragu", alergenos: "glúten, lactose");

        var d = await Sut().ObterAsync(EmpresaId, item.Id);

        d.Ingredientes.Should().Be("massa, ragu");
        d.Alergenos.Should().Be("glúten, lactose");
    }

    [Fact]
    public async Task Arquivar_EIdempotente_ERepoeDepois()
    {
        var item = Item("Lasanha", visivel: true);

        (await Sut().DefinirArquivadoAsync(EmpresaId, item.Id, true)).Arquivado.Should().BeTrue();
        (await Sut().DefinirArquivadoAsync(EmpresaId, item.Id, true)).Arquivado.Should().BeTrue("o clique repetido não repõe");
        item.Visivel.Should().BeTrue("arquivar não mexe no site");
        (await Sut().DefinirArquivadoAsync(EmpresaId, item.Id, false)).Arquivado.Should().BeFalse();

        await _uow.Received(2).CommitAsync();
        await _cardapio.DidNotReceiveWithAnyArgs().RemoveAsync(default!);
    }

    [Fact]
    public async Task Incluir_NasceEmValidacao_EConfirmarLibera()
    {
        CardapioItem? criado = null;
        await _cardapio.AddAsync(Arg.Do<CardapioItem>(i =>
        {
            criado = i;
            _cardapio.GetByIdAndScopeAsync(_vitrine.Id, i.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(i);
        }), Arg.Any<CancellationToken>());

        var r = await Sut().IncluirAsync(EmpresaId, new DadosItemCardapio(
            "Torta de frango", LinhaProduto.PrepararEmCasa, "6 fatias", 60m, null,
            MexerNovidade: true, NovidadeAte: new DateOnly(2026, 10, 20)));

        criado.Should().NotBeNull();
        criado!.EmValidacao.Should().BeTrue("RN-15: o agente não oferece até ela confirmar");
        criado.NovidadeAte.Should().Be(new DateOnly(2026, 10, 20));

        await Sut().ConfirmarValidacaoAsync(EmpresaId, r.ItemId);
        criado.EmValidacao.Should().BeFalse();
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
    public async Task Mover_ComTodosEmpatadosEmZero_RenumeraESobe()
    {
        // Cardápio nunca reordenado: todos nascem com ordem 0 (#1486).
        var a = Item("Lasanha", visivel: true);
        var b = Item("Nhoque", visivel: true);
        var c = Item("Torta", visivel: true);
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns([a, b, c]);
        var naOrdem = new[] { a, b, c }.OrderBy(i => i.CriadoEm).ThenBy(i => i.Id).ToList();
        var segundo = naOrdem[1];

        var r = await Sut().MoverAsync(EmpresaId, segundo.Id, DirecaoMover.Subir);

        r.Ordem.Should().Be(1);
        segundo.OrdemExibicao.Should().Be(1);
        naOrdem[0].OrdemExibicao.Should().Be(2);
        naOrdem[2].OrdemExibicao.Should().Be(3);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Mover_NaPonta_NaoMudaNada()
    {
        var a = Item("Lasanha", visivel: true);
        a.DefinirOrdem(1);
        var b = Item("Nhoque", visivel: true);
        b.DefinirOrdem(2);
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns([a, b]);

        await Sut().MoverAsync(EmpresaId, a.Id, DirecaoMover.Subir);
        await Sut().MoverAsync(EmpresaId, b.Id, DirecaoMover.Descer);

        a.OrdemExibicao.Should().Be(1);
        b.OrdemExibicao.Should().Be(2);
        await _uow.DidNotReceive().CommitAsync();
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
