using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Loja = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Infra.Postgre.IntegrationTests.Workflows;

/// <summary>
/// #2083 A2: o console (Estoque do dia, Produção, Perdas, Planejamento) lê o estoque pelo produto do prato
/// (<c>CardapioItem.ProdutoId</c>). Produto cadastrado no aparelho com o nome do prato virava um Produto novo, sem
/// prato, e a produção feita no APK sumia do console. Agora, sem produto de mesmo nome, ele liga ao produto do prato.
/// </summary>
public class ProductLinkerPratoDoCardapioIntegrationTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    [SkippableTheory]
    [InlineData("Lasanha")]
    [InlineData("  LASANHA ")]
    public async Task NomeDoPrato_LigaAoProdutoDoPrato(string nomeNoApp)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var produtoDoPrato = await ProdutoAsync(s, "Lasanha Bolonhesa 500 g");
        await PratoAsync(s, "lasanha", produtoDoPrato);

        var vinculado = await VincularAsync(s.EmpresaId, nomeNoApp);

        vinculado.Should().Be(produtoDoPrato, "o console lê a produção pelo produto do prato");
        (await ContarProdutosAsync(s.EmpresaId)).Should().Be(1, "nenhum Produto órfão é criado");
    }

    [SkippableFact]
    public async Task DoisPratosComOMesmoNomeEProdutosDiferentes_NaoAdivinhaECriaComoHoje()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var p1 = await ProdutoAsync(s, "Torta de frango P");
        var p2 = await ProdutoAsync(s, "Torta de frango G");
        await PratoAsync(s, "torta", p1);
        await PratoAsync(s, "torta", p2);

        var vinculado = await VincularAsync(s.EmpresaId, "Torta");

        vinculado.Should().NotBeNull().And.NotBe(p1).And.NotBe(p2);
    }

    [SkippableTheory]
    [InlineData("Bolo_", "bolos")]
    [InlineData("Kit%", "kit 50%")]
    public async Task CuringaNoNome_NaoCasaPratoDeOutroNome(string nomeNoApp, string nomeDoPrato)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var produto = await ProdutoAsync(s, "Produto do prato");
        await PratoAsync(s, nomeDoPrato, produto);

        (await VincularAsync(s.EmpresaId, nomeNoApp)).Should().NotBe(produto);
    }

    [SkippableFact]
    public async Task ProdutoComONomeExato_ContinuaVencendoOPrato()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var mesmoNome = await ProdutoAsync(s, "Quiche");
        var doPrato = await ProdutoAsync(s, "Quiche lorraine");
        await PratoAsync(s, "quiche", doPrato);

        (await VincularAsync(s.EmpresaId, "Quiche")).Should().Be(mesmoNome);
    }

    [SkippableFact]
    public async Task PratoArquivado_NaoLiga()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var produto = await ProdutoAsync(s, "Nhoque ao sugo");
        await PratoAsync(s, "nhoque", produto, arquivado: true);

        (await VincularAsync(s.EmpresaId, "Nhoque")).Should().NotBe(produto);
    }

    [SkippableFact]
    public async Task PratoDeOutraEmpresa_NaoLiga()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var outra = await SeedAsync();
        var produtoAlheio = await ProdutoAsync(outra, "Panqueca de carne");
        await PratoAsync(outra, "panqueca", produtoAlheio);
        var s = await SeedAsync();

        (await VincularAsync(s.EmpresaId, "Panqueca")).Should().NotBe(produtoAlheio);
    }

    /// <summary>
    /// A15: o prato tem porções, mas o lote do APK não diz qual. Liga ao produto do prato (o estoque do console
    /// é dele) e não escolhe porção: o produto do APK fica com o produto, nunca com uma variação.
    /// </summary>
    [SkippableFact]
    public async Task PratoComPorcoes_LigaAoProdutoSemEscolherPorcao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var produto = await ProdutoAsync(s, "Arroz de forno");
        var prato = await PratoAsync(s, "arroz", produto);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(CardapioItemVariacao.Criar(prato, "P", 20m, ehPadrao: true));
            seed.Add(CardapioItemVariacao.Criar(prato, "G", 35m, ordemExibicao: 1));
            await seed.SaveChangesAsync();
        }

        (await VincularAsync(s.EmpresaId, "Arroz")).Should().Be(produto);
    }

    [SkippableFact]
    public async Task ProdutoDoAppJaVinculado_NaoTrocaDeVinculo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var antigo = await ProdutoAsync(s, "Feijoada antiga");
        var doPrato = await ProdutoAsync(s, "Feijoada completa");
        await PratoAsync(s, "feijoada", doPrato);

        (await VincularAsync(s.EmpresaId, "Feijoada", erpJaVinculado: antigo)).Should().Be(antigo);
    }

    /// <summary>
    /// #2813 B8: a produção feita no aparelho não aparecia no console, que lê o estoque pelo produto do prato
    /// (<c>CardapioItem.ProdutoId</c>). Prato avulso (sem produto) com o nome do produto do app: o Produto novo do app
    /// nasce ligado ao prato, em vez de órfão.
    /// </summary>
    [SkippableTheory]
    [InlineData("Lasanha")]
    [InlineData("  LASANHA ")]
    public async Task PratoAvulsoSemProduto_ProdutoNovoDoAppLigaOPrato(string nomeNoApp)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var prato = await PratoAsync(s, "lasanha", produtoId: null);

        var vinculado = await VincularAsync(s.EmpresaId, nomeNoApp);

        vinculado.Should().NotBeNull();
        (await ContarProdutosAsync(s.EmpresaId)).Should().Be(1, "o produto do app é criado como hoje, um só");
        (await ProdutoLigadoAoPratoAsync(prato)).Should().Be(vinculado.Value, "o console lê a produção pelo produto do prato");
    }

    [SkippableFact]
    public async Task PratoQueJaTemProduto_NaoETrocadoPeloProdutoNovoDoApp()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var inativo = await ProdutoAsync(s, "Risoto antigo", StatusProduto.Inativo);
        var prato = await PratoAsync(s, "risoto", inativo);

        var vinculado = await VincularAsync(s.EmpresaId, "Risoto");

        vinculado.Should().NotBeNull().And.NotBe(inativo, "o produto inativo do prato não casa: o produto do app é criado como hoje");
        (await ProdutoLigadoAoPratoAsync(prato)).Should().Be(inativo, "prato que já tem produto não é trocado");
    }

    /// <summary>
    /// Dois pratos com o mesmo nome (um avulso, outro já ligado a um produto inativo): não adivinha e não liga o avulso.
    /// Dois avulsos de mesmo nome na mesma loja o banco já barra (<c>uq_cardapio_item_storefront_nome_avulso</c>).
    /// </summary>
    [SkippableFact]
    public async Task DoisPratosComOMesmoNome_NaoLigaOAvulso()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var inativo = await ProdutoAsync(s, "Torta de frango", StatusProduto.Inativo);
        await PratoAsync(s, "torta", inativo);
        var avulso = await PratoAsync(s, "torta", produtoId: null);

        var vinculado = await VincularAsync(s.EmpresaId, "Torta");

        vinculado.Should().NotBeNull().And.NotBe(inativo);
        (await ProdutoLigadoAoPratoAsync(avulso)).Should().BeNull("com dois pratos de mesmo nome não adivinha");
    }

    [SkippableFact]
    public async Task PratoAvulsoDeOutraEmpresa_NaoLiga()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var outra = await SeedAsync();
        var pratoAlheio = await PratoAsync(outra, "panqueca", produtoId: null);
        var s = await SeedAsync();

        (await VincularAsync(s.EmpresaId, "Panqueca")).Should().NotBeNull();
        (await ProdutoLigadoAoPratoAsync(pratoAlheio)).Should().BeNull("prato de outra empresa não é tocado");
    }

    [SkippableFact]
    public async Task PratoAvulsoArquivado_NaoLiga()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();
        var s = await SeedAsync();
        var arquivado = await PratoAsync(s, "nhoque", produtoId: null, arquivado: true);

        (await VincularAsync(s.EmpresaId, "Nhoque")).Should().NotBeNull();
        (await ProdutoLigadoAoPratoAsync(arquivado)).Should().BeNull("prato arquivado não é ligado");
    }

    private sealed record Semente(Guid EmpresaId, Guid CategoriaId, Guid StorefrontId);

    private async Task<Guid?> VincularAsync(Guid empresaId, string nomeNoApp, Guid? erpJaVinculado = null)
    {
        await using (var seed = fixture.CreateDbContext())
        {
            seed.SetMobileTenantContext(empresaId);
            seed.Set<Product>().Add(new Product
            {
                Id = "p-app", Name = nomeNoApp, Category = "pratos", EmpresaId = empresaId, ErpProductId = erpJaVinculado
            });
            await seed.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            db.SetMobileTenantContext(empresaId);
            var linker = new ProductLinker(db, new MobileSystemUserResolver(db), NullLogger<ProductLinker>.Instance,
                new ProdutoRepository(db));
            await linker.ExecuteAsync(["p-app"], empresaId);
            await db.SaveChangesAsync();
        }

        await using var leitura = fixture.CreateDbContext();
        leitura.SetMobileTenantContext(empresaId);
        return (await leitura.Set<Product>().SingleAsync(p => p.Id == "p-app")).ErpProductId;
    }

    private async Task<Guid?> ProdutoLigadoAoPratoAsync(Guid pratoId)
    {
        await using var leitura = fixture.CreateDbContext();
        var prato = await leitura.Set<CardapioItem>().IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == pratoId);
        return prato.ProdutoId;
    }

    private async Task<int> ContarProdutosAsync(Guid empresaId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Produto>().IgnoreQueryFilters().CountAsync(p => p.EmpresaId == empresaId);
    }

    private async Task<Semente> SeedAsync()
    {
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var loja = Loja.Criar(empresaId, $"sf-{empresaId:N}"[..20], "Cardápio", 0m);
        await using var seed = fixture.CreateDbContext();
        seed.Add(new Empresa { Id = empresaId, Nome = "Empresa prato", Documento = $"{Random.Shared.Next(100000, 999999)}" });
        seed.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Cardápio" });
        seed.Add(loja);
        await seed.SaveChangesAsync();
        return new Semente(empresaId, categoriaId, loja.Id);
    }

    private async Task<Guid> ProdutoAsync(Semente s, string nome, StatusProduto status = StatusProduto.Ativo)
    {
        var id = Guid.NewGuid();
        await using var seed = fixture.CreateDbContext();
        seed.Add(new Produto { Id = id, EmpresaId = s.EmpresaId, CategoriaId = s.CategoriaId, Nome = nome, Status = status });
        await seed.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> PratoAsync(Semente s, string nome, Guid? produtoId, bool arquivado = false)
    {
        var prato = CardapioItem.CriarAvulso(s.StorefrontId, nome, 30m);
        if (produtoId.HasValue) prato.VincularProduto(produtoId.Value);
        if (arquivado) prato.Arquivar(DateTime.UtcNow);
        await using var seed = fixture.CreateDbContext();
        seed.Add(prato);
        await seed.SaveChangesAsync();
        return prato.Id;
    }
}
