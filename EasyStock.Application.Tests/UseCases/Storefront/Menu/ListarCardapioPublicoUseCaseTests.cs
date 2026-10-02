using EasyStock.Application.Ports.Output.Persistence;
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
        IItemEstoqueRepository ItemEstoqueRepository,
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

        // #1171: por padrão nenhum produto tem saldo (dicionário vazio = saldo 0).
        var itemEstoqueRepo = Substitute.For<IItemEstoqueRepository>();
        itemEstoqueRepo.GetSaldoDisponivelPorProdutosAsync(
                Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal>());

        var logger = Substitute.For<ILogger<ListarCardapioPublicoUseCase>>();

        return new Fakes(storefrontRepo, cardapioRepo, itemEstoqueRepo, logger, empresaId, storefront);
    }

    private static ListarCardapioPublicoUseCase BuildUseCase(Fakes f) =>
        new(f.StorefrontRepository, f.CardapioItemRepository, f.ItemEstoqueRepository, f.Logger);

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

    // Item AVULSO (ADR-0031): sem Produto vinculado; nome/categoria/preço vêm do próprio item.
    private static CardapioItem CriarItemAvulso(
        Guid storefrontId, string nome, decimal precoReais, string? categoria = null,
        double ordem = 0, bool visivel = true)
    {
        var item = CardapioItem.CriarAvulso(storefrontId, nome, precoReais, categoria);
        item.DefinirOrdem(ordem);
        if (visivel) item.TornarVisivel();
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
        dto.EstoqueAtual.Should().Be(0, "vinculado sem lote com saldo retorna 0; avulsos retornariam null");
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

    // ── Item avulso (ADR-0031) ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AvulsoPreco35Reais_PrecoCentavos3500()
    {
        var f = BuildFakes();
        var avulso = CriarItemAvulso(f.Storefront.Id, "Pão de Alho", 35.00m);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { avulso });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Single().PrecoCentavos.Should().Be(3500,
            "pina a conversão R$→centavos (×100) da projeção para item avulso, não só o domínio");
    }

    [Fact]
    public async Task ExecuteAsync_AvulsoDisponivel_EstoqueAtualNuloEDisponivelTrue()
    {
        var f = BuildFakes();
        var avulso = CriarItemAvulso(f.Storefront.Id, "Pão de Alho", 18.00m);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { avulso });

        var dto = (await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido))).Itens.Single();

        dto.EstoqueAtual.Should().BeNull("avulso (ProdutoId null) não tem snapshot de estoque do ERP");
        dto.Disponivel.Should().BeTrue("o front usa Disponivel, não EstoqueAtual, para 'Esgotado'");
    }

    [Fact]
    public async Task ExecuteAsync_AvulsoComCategoria_AplicaTitleCasePtBr()
    {
        var f = BuildFakes();
        var avulso = CriarItemAvulso(f.Storefront.Id, "Pão de Alho", 18.00m, categoria: "Acompanhamentos");
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { avulso });

        var dto = (await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido))).Itens.Single();

        // NomePublico/CategoriaTexto avulsos são armazenados lowercase (factory); a vitrine
        // pública aplica title-case pt-BR (5b84447b, #643) — preposição "de" fica minúscula.
        dto.Nome.Should().Be("Pão de Alho", "avulso lowercase recebe title-case pt-BR na vitrine");
        dto.Categoria.Should().Be("Acompanhamentos", "CategoriaTexto avulso lowercase recebe title-case na vitrine");
    }

    [Theory]
    [InlineData("molho pomodoro 500 ml", "Molho Pomodoro 500 ml")]
    [InlineData("capeletti de carne 1 kg", "Capeletti de Carne 1 kg")]
    [InlineData("nhoque artesanal 500 g", "Nhoque Artesanal 500 g")]
    [InlineData("suco de uva 1 l", "Suco de Uva 1 l")]
    public async Task ExecuteAsync_AvulsoComUnidade_MantemUnidadeMinuscula(string armazenado, string esperado)
    {
        // #1334: o cardápio real tem um item por tamanho; "500 G" e "250 Ml" chegavam ao cliente e ao agente.
        var f = BuildFakes();
        var avulso = CriarItemAvulso(f.Storefront.Id, armazenado, 18.00m);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { avulso });

        var dto = (await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido))).Itens.Single();

        dto.Nome.Should().Be(esperado);
    }

    [Fact]
    public async Task ExecuteAsync_MixAvulsoVinculado_OrdenaSemExcecao()
    {
        var f = BuildFakes();
        var vinculado = CriarItem(f.Storefront.Id, f.EmpresaId, "Lasanha",
            precoReferencia: 42m, categoriaNome: "Pratos", ordem: 1.0);
        var avulso = CriarItemAvulso(f.Storefront.Id, "Pão de Alho", 18.00m,
            categoria: "acompanhamentos", ordem: 1.0);

        // Repo retorna mistura fora de ordem; o sort-key usa CategoriaEfetiva() (cobre avulso + vinculado).
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { vinculado, avulso });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens.Should().HaveCount(2, "mix avulso+vinculado não lança e mantém ambos");
    }

    // ── S15: linha e tempo de preparo no contrato público ─────────────

    [Fact]
    public async Task EmiteLinhaETempo()
    {
        var f = BuildFakes();
        var item = CriarItemAvulso(f.Storefront.Id, "nhoque congelado", 30m);
        item.DefinirPreparo(EasyStock.Domain.Enums.Storefront.LinhaProduto.PrepararEmCasa, 45, null);
        var semLinha = CriarItemAvulso(f.Storefront.Id, "lasanha", 35m, ordem: 1);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { item, semLinha });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens[0].Linha.Should().Be("prepararEmCasa");
        result.Itens[0].TempoPreparoMinutos.Should().Be(45);
        result.Itens[1].Linha.Should().Be("paraServir");
        result.Itens[1].TempoPreparoMinutos.Should().BeNull();
    }
    // ── #1171: saldo produzido projetado no cardápio ───────────────────

    [Fact]
    public async Task ExecuteAsync_VinculadoComSaldo_ProjetaSaldoDoEstoque()
    {
        var f = BuildFakes();
        var item = CriarItem(f.Storefront.Id, f.EmpresaId, "Lasanha", 42m);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { item });
        f.ItemEstoqueRepository.GetSaldoDisponivelPorProdutosAsync(
                f.EmpresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [item.ProdutoId!.Value] = 12m });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens[0].EstoqueAtual.Should().Be(12, "saldo produzido pelo POST api/producao aparece no cardápio");
        result.Itens[0].Disponivel.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_VinculadoSemSaldo_EstoqueZeroMasDisponivelSegueFlagManual()
    {
        // Decisão #1171: Disponivel continua flag manual. Saldo 0 não esgota o item, porque o
        // checkout recusa item !Disponivel (#1158) e falta de estoque avisa, não trava (S17).
        var f = BuildFakes();
        var item = CriarItem(f.Storefront.Id, f.EmpresaId, "Nhoque", 30m);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { item });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens[0].EstoqueAtual.Should().Be(0);
        result.Itens[0].Disponivel.Should().BeTrue("saldo zero não derruba a disponibilidade manual");
    }

    [Fact]
    public async Task ExecuteAsync_VinculadoEsgotadoManualComSaldo_MantemIndisponivel()
    {
        var f = BuildFakes();
        var item = CriarItem(f.Storefront.Id, f.EmpresaId, "Torta", 50m, disponivel: false);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { item });
        f.ItemEstoqueRepository.GetSaldoDisponivelPorProdutosAsync(
                f.EmpresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [item.ProdutoId!.Value] = 3m });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens[0].EstoqueAtual.Should().Be(3);
        result.Itens[0].Disponivel.Should().BeFalse("o esgotado manual prevalece sobre o saldo");
    }

    [Fact]
    public async Task ExecuteAsync_SaldoFracionado_ArredondaParaBaixo()
    {
        var f = BuildFakes();
        var item = CriarItem(f.Storefront.Id, f.EmpresaId, "Queijo", 20m);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { item });
        f.ItemEstoqueRepository.GetSaldoDisponivelPorProdutosAsync(
                f.EmpresaId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, decimal> { [item.ProdutoId!.Value] = 2.75m });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens[0].EstoqueAtual.Should().Be(2, "o cardápio não promete fração que não existe inteira");
    }

    [Fact]
    public async Task ExecuteAsync_ConsultaSaldosUmaVezComEmpresaDoStorefrontESoVinculados()
    {
        var f = BuildFakes();
        var a = CriarItem(f.Storefront.Id, f.EmpresaId, "Lasanha", 42m);
        var b = CriarItem(f.Storefront.Id, f.EmpresaId, "Nhoque", 30m, ordem: 1);
        var avulso = CriarItemAvulso(f.Storefront.Id, "bolo", 20m, ordem: 2);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { a, b, avulso });

        await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        await f.ItemEstoqueRepository.Received(1).GetSaldoDisponivelPorProdutosAsync(
            f.EmpresaId,
            Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 2 && ids.Contains(a.ProdutoId!.Value) && ids.Contains(b.ProdutoId!.Value)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_SemItemVinculado_NaoConsultaEstoque()
    {
        var f = BuildFakes();
        var avulso = CriarItemAvulso(f.Storefront.Id, "bolo", 20m);
        f.CardapioItemRepository.GetVisiveisDoStorefrontAsync(f.Storefront.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { avulso });

        var result = await BuildUseCase(f).ExecuteAsync(new ListarCardapioPublicoInput(SlugValido));

        result.Itens[0].EstoqueAtual.Should().BeNull();
        await f.ItemEstoqueRepository.DidNotReceiveWithAnyArgs().GetSaldoDisponivelPorProdutosAsync(
            default, default!, default);
    }
}
