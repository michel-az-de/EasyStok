using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Application.Tests.UseCases.Storefront.Menu;

/// <summary>
/// Testes do <see cref="ListarCardapioPublicoUseCase"/> (EZ-MENU-001).
///
/// <para>
/// Cobertura:
/// </para>
/// <list type="bullet">
///   <item>Happy path — storefront ativo, items visíveis retornados como DTOs.</item>
///   <item>Storefront inexistente → <see cref="StorefrontNaoEncontradoException"/>.</item>
///   <item>Storefront inativo → <see cref="StorefrontNaoEncontradoException"/> (não vaza distinção).</item>
///   <item>Storefront sem items → lista vazia (não erro).</item>
///   <item>Repo já filtra Visivel=true — apenas valida que use case respeita o contrato.</item>
///   <item>PrecoEfetivo: <c>PrecoStorefront</c> override OU <c>Produto.PrecoReferencia</c>.</item>
///   <item>Ordenação: categoria ASC → ordemExibicao ASC.</item>
///   <item>DTO não expõe <c>EmpresaId</c>, <c>CustoReferencia</c>, <c>FornecedorId</c>.</item>
/// </list>
/// </summary>
public class ListarCardapioPublicoUseCaseTests
{
    private const string SlugValido = "casa-da-baba";

    private sealed record Fakes(
        IStorefrontRepository StorefrontRepository,
        ICardapioItemRepository CardapioItemRepository,
        ILogger<ListarCardapioPublicoUseCase> Logger,
        Guid EmpresaId,
        StorefrontEntity Storefront);

    private static Fakes BuildFakes(bool storefrontAtivo = true)
    {
        var empresaId = Guid.NewGuid();
        var storefront = StorefrontEntity.Criar(
            empresaId: empresaId,
            slug: SlugValido,
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 0m);
        if (storefrontAtivo) storefront.Ativar();

        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns(storefront);

        var cardapioRepo = Substitute.For<ICardapioItemRepository>();
        cardapioRepo.GetVisiveisDoStorefrontAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CardapioItem>());

        var logger = Substitute.For<ILogger<ListarCardapioPublicoUseCase>>();

        return new Fakes(storefrontRepo, cardapioRepo, logger, empresaId, storefront);
    }

    private static ListarCardapioPublicoUseCase BuildUseCase(Fakes f) =>
        new(f.StorefrontRepository, f.CardapioItemRepository, f.Logger);

    private static CardapioItem CriarItem(
        Guid storefrontId,
        Guid empresaId,
        string nome,
        decimal precoReferencia,
        decimal? precoStorefront = null,
        string? descricao = null,
        string? fotoUrl = null,
        string? categoriaNome = null,
        double ordem = 0,
        string? tag = null,
        bool visivel = true,
        bool disponivel = true)
    {
        var categoria = categoriaNome is null ? null : new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = categoriaNome,
        };

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoria?.Id ?? Guid.NewGuid(),
            Nome = nome,
            Tipo = TipoProduto.Alimento,
            PrecoReferencia = Dinheiro.FromDecimal(precoReferencia),
            Categoria = categoria,
            Status = StatusProduto.Ativo,
        };

        var item = CardapioItem.CriarAPartirDeProduto(storefrontId, produto);
        // O repo de produção (GetVisiveisDoStorefrontAsync) carrega a navegação Produto
        // via Include; o use case lê i.Produto?.Nome / PrecoEfetivo(). O mock precisa
        // popular a nav pra refletir produção — senão Nome sai "" e Preco 0 (test-fidelity).
        item.Produto = produto;
        item.AtualizarMetadata(
            descricaoPublica: descricao,
            fotoUrl: fotoUrl,
            precoStorefront: precoStorefront,
            tag: tag);
        item.DefinirOrdem(ordem);
        if (visivel) item.TornarVisivel();
        if (!disponivel) item.MarcarEsgotado();
        return item;
    }

    // ── Happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPath_RetornaItensComoDto()
    {
        var f = BuildFakes();
        var item = CriarItem(
            storefrontId: f.Storefront.Id,
            empresaId: f.EmpresaId,
            nome: "Lasanha de berinjela",
            precoReferencia: 42.50m,
            descricao: "Massa fresca com molho da casa",
            fotoUrl: "https://cdn/lasanha.jpg",
            categoriaNome: "Pratos principais",
            ordem: 1.0,
            tag: "vegetariano");

        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { item });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Should().HaveCount(1);
        var dto = result.Itens[0];
        dto.Id.Should().Be(item.Id);
        dto.Nome.Should().Be("Lasanha de berinjela");
        dto.Descricao.Should().Be("Massa fresca com molho da casa");
        dto.PrecoCentavos.Should().Be(4250);
        dto.ImagemUrl.Should().Be("https://cdn/lasanha.jpg");
        dto.Categoria.Should().Be("Pratos principais");
        dto.Ordem.Should().Be(1.0);
        dto.Tag.Should().Be("vegetariano");
        dto.Disponivel.Should().BeTrue();
        dto.EstoqueAtual.Should().Be(0, "vinculado com ProdutoId retorna 0 (snapshot eventual); avulsos retornariam null");
    }

    // ── Storefront inexistente / inativo ───────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StorefrontInexistente_LancaStorefrontNaoEncontrado()
    {
        var f = BuildFakes();
        f.StorefrontRepository.GetBySlugAsync(SlugValido, Arg.Any<CancellationToken>())
            .Returns((StorefrontEntity?)null);

        var act = () => BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
        await f.CardapioItemRepository.DidNotReceive().GetVisiveisDoStorefrontAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_StorefrontInativo_LancaStorefrontNaoEncontrado()
    {
        var f = BuildFakes(storefrontAtivo: false);

        var act = () => BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>(
            "storefront inativo é equivalente a inexistente para o público");
        await f.CardapioItemRepository.DidNotReceive().GetVisiveisDoStorefrontAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Lista vazia ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StorefrontSemItens_RetornaListaVazia()
    {
        var f = BuildFakes();
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CardapioItem>());

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Should().BeEmpty();
    }

    // ── Preço efetivo ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PrecoStorefront_OverrideTemPrioridadeSobrePrecoReferencia()
    {
        var f = BuildFakes();
        var item = CriarItem(
            storefrontId: f.Storefront.Id,
            empresaId: f.EmpresaId,
            nome: "Bolo cenoura",
            precoReferencia: 30m,
            precoStorefront: 25m); // override
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { item });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Single().PrecoCentavos.Should().Be(2500, "PrecoStorefront override deve prevalecer");
    }

    [Fact]
    public async Task ExecuteAsync_SemPrecoStorefront_UsaPrecoReferenciaDoProduto()
    {
        var f = BuildFakes();
        var item = CriarItem(
            storefrontId: f.Storefront.Id,
            empresaId: f.EmpresaId,
            nome: "Pudim",
            precoReferencia: 18m); // sem override
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { item });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Single().PrecoCentavos.Should().Be(1800);
    }

    // ── Ordenação ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Ordenacao_CategoriaAscDepoisOrdemAsc()
    {
        var f = BuildFakes();
        var bolo = CriarItem(f.Storefront.Id, f.EmpresaId, "Bolo de cenoura",
            precoReferencia: 18m, categoriaNome: "Sobremesas", ordem: 2.0);
        var pudim = CriarItem(f.Storefront.Id, f.EmpresaId, "Pudim",
            precoReferencia: 14m, categoriaNome: "Sobremesas", ordem: 1.0);
        var lasanha = CriarItem(f.Storefront.Id, f.EmpresaId, "Lasanha",
            precoReferencia: 42m, categoriaNome: "Pratos principais", ordem: 1.0);

        // Repo retorna fora de ordem propositalmente — use case deve reordenar.
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { bolo, lasanha, pudim });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Should().HaveCount(3);
        result.Itens[0].Nome.Should().Be("Lasanha", "Pratos principais < Sobremesas");
        result.Itens[1].Nome.Should().Be("Pudim", "ordem 1.0 antes de 2.0 dentro de Sobremesas");
        result.Itens[2].Nome.Should().Be("Bolo de cenoura");
    }

    [Fact]
    public async Task ExecuteAsync_ItemSemCategoria_NaoQuebraOrdenacao()
    {
        var f = BuildFakes();
        var semCategoria = CriarItem(f.Storefront.Id, f.EmpresaId, "Sem categoria",
            precoReferencia: 10m, categoriaNome: null, ordem: 1.0);
        var comCategoria = CriarItem(f.Storefront.Id, f.EmpresaId, "Com categoria",
            precoReferencia: 20m, categoriaNome: "Z-bebidas", ordem: 1.0);

        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { semCategoria, comCategoria });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Should().HaveCount(2);
    }

    // ── Item esgotado ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ItemEsgotado_MarcaDisponivelFalseMasMantemNaLista()
    {
        var f = BuildFakes();
        var esgotado = CriarItem(f.Storefront.Id, f.EmpresaId, "Esgotado",
            precoReferencia: 10m, disponivel: false);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { esgotado });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Should().HaveCount(1);
        result.Itens[0].Disponivel.Should().BeFalse(
            "esgotado é sinal visual; item continua visível pra cliente saber que existe");
    }

    // ── Repo respeita Visivel=true ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DelegaFiltroVisivelParaRepository()
    {
        // Use case confia que GetVisiveisDoStorefrontAsync já filtra Visivel=true.
        // Esse teste é contrato — se o repo for alterado pra retornar não-visíveis,
        // outro lugar quebra; aqui validamos apenas que NÃO chamamos GetTodos.
        var f = BuildFakes();
        await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        await f.CardapioItemRepository.Received(1)
            .GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>());
        await f.CardapioItemRepository.DidNotReceive()
            .GetTodosDoStorefrontAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
