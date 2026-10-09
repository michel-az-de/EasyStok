using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>
/// M1.3 (#1483): categorias do cardápio (seções) pelo console. Nome único, ordem renumerada no
/// servidor, só a vazia é excluída, e a migração do texto antigo é idempotente.
/// </summary>
public class SecoesDoCardapioComandaUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly StorefrontEntity _vitrine = StorefrontEntity.Criar(EmpresaId, "casa-da-baba", "Casa da Baba", 0m);
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioSecaoRepository _secoesRepo = Substitute.For<ICardapioSecaoRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly List<CardapioSecao> _secoes = [];
    private readonly List<CardapioItem> _itens = [];

    public SecoesDoCardapioComandaUseCaseTests()
    {
        _vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(EmpresaId, Arg.Any<CancellationToken>()).Returns(_vitrine);
        _secoesRepo.GetDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns(_ => _secoes.ToList());
        _secoesRepo.AddAsync(Arg.Do<CardapioSecao>(s => _secoes.Add(s)), Arg.Any<CancellationToken>());
        _secoesRepo.ContarItensPorSecaoAsync(_vitrine.Id, Arg.Any<CancellationToken>())
            .Returns(_ => _itens.Where(i => i.SecaoId != null).GroupBy(i => i.SecaoId!.Value).ToDictionary(g => g.Key, g => g.Count()));
        _cardapio.GetTodosDoStorefrontAsync(_vitrine.Id, Arg.Any<CancellationToken>()).Returns(_ => _itens.ToList());
    }

    private SecoesDoCardapioComandaUseCase Sut() => new(_storefronts, _secoesRepo, _cardapio, _uow);

    private CardapioSecao Secao(string nome, double ordem = 0)
    {
        var s = CardapioSecao.CriarRaiz(_vitrine.Id, nome, ordem);
        _secoes.Add(s);
        return s;
    }

    private CardapioItem Item(string nome, string? categoria)
    {
        var item = CardapioItem.CriarAvulso(_vitrine.Id, nome, 30m, categoria);
        _itens.Add(item);
        _cardapio.GetByIdAndScopeAsync(_vitrine.Id, item.Id, EmpresaId, Arg.Any<CancellationToken>()).Returns(item);
        return item;
    }

    [Fact]
    public async Task Criar_NomeRepetido_EmQualquerCaixa_400()
    {
        Secao("Massas");

        var act = () => Sut().CriarAsync(EmpresaId, "massas");

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*Já existe*");
    }

    [Fact]
    public async Task Criar_EntraNoFimDaLista()
    {
        Secao("Massas", 1);
        Secao("Molhos", 2);

        var nova = await Sut().CriarAsync(EmpresaId, "Sobremesas");

        nova.Ordem.Should().Be(3);
        (await Sut().ListarAsync(EmpresaId)).Select(s => s.Nome).Should().Equal("Massas", "Molhos", "Sobremesas");
    }

    [Fact]
    public async Task Excluir_ComPratoDentro_Recusa_EVaziaSai()
    {
        var cheia = Secao("Massas");
        var vazia = Secao("Bebidas");
        Item("Lasanha", null).DefinirSecao(cheia.Id);

        var act = () => Sut().ExcluirAsync(EmpresaId, cheia.Id);
        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*1 prato*");

        await Sut().ExcluirAsync(EmpresaId, vazia.Id);
        await _secoesRepo.Received(1).RemoveAsync(vazia, Arg.Any<CancellationToken>());
        await _secoesRepo.DidNotReceive().RemoveAsync(cheia, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Mover_ComOrdemZero_Renumera()
    {
        var a = Secao("Massas");
        var b = Secao("Molhos");

        var naOrdem = new[] { a, b }.OrderBy(s => s.CriadoEm).ThenBy(s => s.Id).ToList();
        await Sut().MoverAsync(EmpresaId, naOrdem[1].Id, DirecaoMover.Subir);

        naOrdem[1].OrdemExibicao.Should().Be(1);
        naOrdem[0].OrdemExibicao.Should().Be(2);
    }

    [Fact]
    public async Task MigrarCategorias_CriaUmaPorTexto_LigaOsPratos_EEIdempotente()
    {
        var lasanha = Item("Lasanha", "Massas");
        var nhoque = Item("Nhoque", "MASSAS");
        var molho = Item("Sugo", "molhos");
        var semCategoria = Item("Torta", null);

        var r = await Sut().MigrarCategoriasAsync(EmpresaId);

        r.SecoesCriadas.Should().Be(2);
        r.ItensLigados.Should().Be(3);
        _secoes.Select(s => s.Nome).Should().BeEquivalentTo("Massas", "Molhos");
        lasanha.SecaoId.Should().NotBeNull();
        nhoque.SecaoId.Should().Be(lasanha.SecaoId);
        (molho.SecaoId == lasanha.SecaoId).Should().BeFalse("molhos é outra categoria");
        semCategoria.SecaoId.Should().BeNull();
        lasanha.CategoriaTexto.Should().Be("massas", "o texto antigo não é apagado");

        var deNovo = await Sut().MigrarCategoriasAsync(EmpresaId);
        deNovo.Should().Be(new MigracaoDeCategoriasResult(0, 0));
        _secoes.Should().HaveCount(2);
    }

    [Fact]
    public async Task MigrarCategorias_UsaASecaoQueJaExiste()
    {
        var massas = Secao("Massas", 1);
        var lasanha = Item("Lasanha", "massas");

        var r = await Sut().MigrarCategoriasAsync(EmpresaId);

        r.Should().Be(new MigracaoDeCategoriasResult(0, 1));
        _secoes.Should().ContainSingle();
        lasanha.SecaoId.Should().Be(massas.Id);
    }
}
