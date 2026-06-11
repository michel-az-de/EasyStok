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

    // ── Modo AVULSO (ADR-0031) ─────────────────────────────────────────

    [Fact]
    public async Task DeveAdicionar_ItemAvulso_SemVinculoComProduto()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-av", "AV", 0m);
        _storefrontRepo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);

        CardapioItem? capturado = null;
        await _cardapioRepo.AddAsync(
            Arg.Do<CardapioItem>(c => capturado = c), Arg.Any<CancellationToken>());

        var result = await Sut().ExecuteAsync(NewCommandAvulso(s.Id, nome: "Pão de Alho", preco: 18m));

        result.ItemId.Should().NotBeEmpty();
        capturado.Should().NotBeNull();
        capturado!.ProdutoId.Should().BeNull("avulso não vincula produto");
        capturado.NomePublico.Should().Be("pão de alho", "factory CriarAvulso armazena em lowercase");
        capturado.PrecoStorefront.Should().Be(18m);
        capturado.Visivel.Should().BeFalse("default seguro: tenant publica manualmente");
        await _uow.Received(1).CommitAsync();
        // Modo avulso nunca consulta o repositório de produtos.
        await _produtoRepo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
    }

    [Fact]
    public async Task DeveLancar_AvulsoSemNome()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-sn", "SN", 0m);
        _storefrontRepo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);

        var act = async () => await Sut().ExecuteAsync(NewCommandAvulso(s.Id, nome: null));
        await act.Should().ThrowAsync<UseCaseValidationException>()
            .Where(e => e.Code == "NOME_OBRIGATORIO");
    }

    [Fact]
    public async Task DeveLancar_AvulsoSemPreco()
    {
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-sp", "SP", 0m);
        _storefrontRepo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);

        var act = async () => await Sut().ExecuteAsync(NewCommandAvulso(s.Id, preco: null));
        await act.Should().ThrowAsync<UseCaseValidationException>()
            .Where(e => e.Code == "PRECO_OBRIGATORIO");
    }

    [Fact]
    public async Task DeveLancar_QuandoEmpresaIdNaoBateComStorefront()
    {
        // Escopo de tenant (Slice 1 — fix IDOR): Admin de outra empresa não opera storefront alheio.
        var s = StorefrontEntity.Criar(Guid.NewGuid(), "slug-esc", "ESC", 0m);
        _storefrontRepo.GetByIdAsync(s.Id, Arg.Any<CancellationToken>()).Returns(s);

        var act = async () => await Sut().ExecuteAsync(NewCommandAvulso(s.Id, empresaId: Guid.NewGuid()));
        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>(
            "storefront de outra empresa retorna 404, não vaza existência");
    }

    private static AdicionarCardapioItemAdminCommand NewCommand(Guid storefrontId, Guid? produtoId = null) =>
        new(storefrontId,
            ProdutoId: produtoId ?? Guid.NewGuid(),
            NomePublico: null,
            CategoriaTexto: null,
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

    private static AdicionarCardapioItemAdminCommand NewCommandAvulso(
        Guid storefrontId, string? nome = "Pão de Alho", decimal? preco = 18m, Guid? empresaId = null) =>
        new(storefrontId,
            ProdutoId: null,                 // avulso
            NomePublico: nome,
            CategoriaTexto: "acompanhamentos",
            OrdemExibicao: 1.0,
            Visivel: false,
            DescricaoPublica: null,
            Ingredientes: null,
            Alergenos: null,
            SugestaoMolho: null,
            TempoPreparo: null,
            FotoUrl: null,
            PrecoStorefront: preco,
            Tag: null,
            PesoExibicao: null,
            FiltrosJson: null,
            EmpresaId: empresaId);
}
