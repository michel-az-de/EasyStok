using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Admin.Storefront.Cardapio.AdicionarCardapioItemAdmin;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Admin.Storefront.Cardapio;

public class AdicionarCardapioItemAdminUseCaseTests
{
    private readonly IStorefrontRepository _storefrontRepo = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapioRepo = Substitute.For<ICardapioItemRepository>();
    private readonly IProdutoRepository _produtoRepo = Substitute.For<IProdutoRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private AdicionarCardapioItemAdminUseCase Sut() => new(_storefrontRepo, _cardapioRepo, _produtoRepo, _uow);

    private static Produto ProdutoFake(Guid empresaId, Guid id, string nome = "Lasanha")
    {
        var p = (Produto)System.Activator.CreateInstance(typeof(Produto), nonPublic: true)!;
        typeof(Produto).GetProperty("Id")!.SetValue(p, id);
        typeof(Produto).GetProperty("EmpresaId")!.SetValue(p, empresaId);
        typeof(Produto).GetProperty("Nome")!.SetValue(p, nome);
        return p;
    }

    [Fact]
    public async Task DeveLancar_QuandoStorefrontNaoExiste()
    {
        var storefrontId = Guid.NewGuid();
        _storefrontRepo.GetByIdAsync(storefrontId, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var act = async () => await Sut().ExecuteAsync(NewCommand(storefrontId));
        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task DeveLancar_QuandoProdutoNaoPertenceAEmpresa()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-pp", "PP", 0m);
        _storefrontRepo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);
        // Repo retorna null porque o produto não pertence à EmpresaId do storefront
        _produtoRepo.GetByIdAsync(s.EmpresaId, Arg.Any<Guid>()).Returns((Produto?)null);

        var act = async () => await Sut().ExecuteAsync(NewCommand(s.Id));
        await act.Should().ThrowAsync<UseCaseValidationException>()
            .Where(e => e.Code == "PRODUTO_INEXISTENTE");
    }

    [Fact]
    public async Task DeveLancar_QuandoProdutoJaNoCardapio()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-jc", "JC", 0m);
        var produtoId = Guid.NewGuid();
        var produto = ProdutoFake(s.EmpresaId, produtoId);

        _storefrontRepo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);
        _produtoRepo.GetByIdAsync(s.EmpresaId, produtoId).Returns(produto);
        _cardapioRepo.GetProdutoIdsDoStorefrontAsync(s.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { produtoId });

        var act = async () => await Sut().ExecuteAsync(NewCommand(s.Id, produtoId));
        await act.Should().ThrowAsync<UseCaseValidationException>()
            .Where(e => e.Code == "PRODUTO_JA_NO_CARDAPIO");
    }

    [Fact]
    public async Task DeveAdicionar_QuandoOk()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-ok", "OK", 0m);
        var produtoId = Guid.NewGuid();
        var produto = ProdutoFake(s.EmpresaId, produtoId, "Lasanha Família");

        _storefrontRepo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);
        _produtoRepo.GetByIdAsync(s.EmpresaId, produtoId).Returns(produto);
        _cardapioRepo.GetProdutoIdsDoStorefrontAsync(s.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Guid>());

        CardapioItem? capturado = null;
        await _cardapioRepo.AddAsync(
            Arg.Do<CardapioItem>(c => capturado = c), Arg.Any<CancellationToken>());

        var result = await Sut().ExecuteAsync(NewCommand(s.Id, produtoId));

        result.ItemId.Should().NotBeEmpty();
        capturado.Should().NotBeNull();
        capturado!.StorefrontId.Should().Be(s.Id);
        capturado.ProdutoId.Should().Be(produtoId);
        capturado.Visivel.Should().BeFalse(); // default seguro
        await _uow.Received(1).CommitAsync();
    }

    private static AdicionarCardapioItemAdminCommand NewCommand(Guid storefrontId, Guid? produtoId = null) =>
        new(storefrontId,
            produtoId ?? Guid.NewGuid(),
            OrdemExibicao: 1.0,
            Visivel: false,
            DescricaoPublica: null,
            Ingredientes: null,
            Alergenos: null,
            SugestaoMolho: null,
            TempoPreparo: null,
            FotoUrl: null,
            PrecoStorefront: null,
            Tag: null,
            PesoExibicao: null,
            FiltrosJson: null);
}
