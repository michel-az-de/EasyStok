using EasyStock.Application.UseCases.Caixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2738: produto vendido por peso (0,5 kg) nao pode ser truncado para 0 nos totais de analytics.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class AnalyticsQuantidadeFracionadaTests(PostgreSqlDatabaseFixture fixture)
{
    private async Task<(AnalyticsRepository Repo, Guid EmpresaId)> PrepararVendaAsync()
    {
        var dbContext = fixture.CreateDbContext();
        var repo = new AnalyticsRepository(dbContext, new CaixaSaldoCalculator(new CaixaRepository(dbContext)));

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Fracionada", Documento = empresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria", CriadoEm = agora, AlteradoEm = agora });
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Produto por peso",
            Tipo = TipoProduto.Fisico, SkuBase = CodigoSku.From("SKU-PESO"), Status = StatusProduto.Ativo,
            CriadoEm = agora, AlteradoEm = agora
        };
        dbContext.Produtos.Add(produto);
        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10), QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m), EntradaEm = agora, Status = StatusItemEstoque.Ok,
            CriadoEm = agora, AlteradoEm = agora
        };
        dbContext.ItensEstoque.Add(item);
        var venda = new Venda
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Canal = CanalVenda.LojaPropria,
            Natureza = NaturezaMovimentacaoEstoque.Venda, DataVenda = agora,
            ValorTotal = Dinheiro.FromDecimal(100m), CriadoEm = agora
        };
        dbContext.Vendas.Add(venda);
        for (var i = 0; i < 2; i++)
        {
            dbContext.ItensVenda.Add(new ItemVenda
            {
                Id = Guid.NewGuid(), VendaId = venda.Id, ItemEstoqueId = item.Id, ProdutoId = produto.Id,
                Quantidade = Quantidade.From(0.5m), PrecoUnitario = Dinheiro.FromDecimal(100m),
                PrecoTotal = Dinheiro.FromDecimal(50m), CriadoEm = agora
            });
            dbContext.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, ItemEstoqueId = item.Id, ProdutoId = produto.Id,
                Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda,
                Quantidade = Quantidade.From(0.5m), ValorUnitario = Dinheiro.FromDecimal(100m),
                ValorTotal = Dinheiro.FromDecimal(50m), DataMovimentacao = agora, CriadoEm = agora
            });
        }
        await dbContext.SaveChangesAsync();
        return (repo, empresaId);
    }

    [SkippableFact]
    public async Task GetReceitaPorPeriodoAsync_SomaItensFracionadosSemTruncar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (repo, empresaId) = await PrepararVendaAsync();

        var result = await repo.GetReceitaPorPeriodoAsync(empresaId, 12);

        ((decimal)result.Single().TotalItensVendidos).Should().Be(1m);
    }

    [SkippableFact]
    public async Task GetVendasPorCanalAsync_SomaItensFracionadosSemTruncar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (repo, empresaId) = await PrepararVendaAsync();

        var result = await repo.GetVendasPorCanalAsync(empresaId, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(1));

        ((decimal)result.Single().TotalItensVendidos).Should().Be(1m);
    }

    [SkippableFact]
    public async Task GetMargemPorProdutoAsync_SomaQuantidadeFracionadaSemTruncar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (repo, empresaId) = await PrepararVendaAsync();

        var (items, _) = await repo.GetMargemPorProdutoAsync(empresaId, 30);

        ((decimal)items.Single().QuantidadeVendida).Should().Be(1m);
    }

    /// <summary>#2750: tres saidas fracionadas (0,5 + 0,5 + 1,5) do mesmo produto no mesmo mes.</summary>
    private async Task<(MovimentacaoEstoqueRepository MovRepo, AnalyticsRepository Analytics, Guid EmpresaId, Guid ProdutoId)> PrepararSaidasMensaisAsync()
    {
        var dbContext = fixture.CreateDbContext();
        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Sazonal", Documento = empresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria", CriadoEm = agora, AlteradoEm = agora });
        var produto = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "Produto sazonal",
            Tipo = TipoProduto.Fisico, SkuBase = CodigoSku.From("SKU-SAZ"), Status = StatusProduto.Ativo,
            CriadoEm = agora, AlteradoEm = agora
        };
        dbContext.Produtos.Add(produto);
        var item = new ItemEstoque
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10), QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m), EntradaEm = agora, Status = StatusItemEstoque.Ok,
            CriadoEm = agora, AlteradoEm = agora
        };
        dbContext.ItensEstoque.Add(item);
        foreach (var quantidade in new[] { 0.5m, 0.5m, 1.5m })
        {
            dbContext.MovimentacoesEstoque.Add(new MovimentacaoEstoque
            {
                Id = Guid.NewGuid(), EmpresaId = empresaId, ItemEstoqueId = item.Id, ProdutoId = produto.Id,
                Tipo = TipoMovimentacaoEstoque.Saida, Natureza = NaturezaMovimentacaoEstoque.Venda,
                Quantidade = Quantidade.From(quantidade), ValorUnitario = Dinheiro.FromDecimal(100m),
                ValorTotal = Dinheiro.FromDecimal(100m * quantidade), DataMovimentacao = agora, CriadoEm = agora
            });
        }
        await dbContext.SaveChangesAsync();
        return (new MovimentacaoEstoqueRepository(dbContext),
            new AnalyticsRepository(dbContext, new CaixaSaldoCalculator(new CaixaRepository(dbContext))),
            empresaId, produto.Id);
    }

    [SkippableFact]
    public async Task GetAgregacaoMensalAsync_SomaSaidasFracionadasSemTruncar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (movRepo, _, empresaId, produtoId) = await PrepararSaidasMensaisAsync();

        var mes = (await movRepo.GetAgregacaoMensalAsync(empresaId, produtoId, 12)).Single();

        ((decimal)mes.TotalSaidas).Should().Be(2.5m);
    }

    [SkippableFact]
    public async Task GetSazonalidadeAsync_SomaSaidasFracionadasSemTruncar()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (_, analytics, empresaId, produtoId) = await PrepararSaidasMensaisAsync();

        var mes = (await analytics.GetSazonalidadeAsync(empresaId, produtoId, 12)).Single();

        ((decimal)mes.TotalSaidas).Should().Be(2.5m);
        mes.MediaMovelTresMeses.Should().Be(2.5m);
    }
}
