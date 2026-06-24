using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class AnalyticsRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task GetDashboardResumoAsync_DeveRetornarResumoCorreto()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            PrecoVendaSugerido = Dinheiro.FromDecimal(75m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Canal = CanalVenda.LojaPropria,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            DataVenda = DateTime.UtcNow,
            ValorTotal = Dinheiro.FromDecimal(150m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.Vendas.Add(venda);

        var itemVenda = new ItemVenda
        {
            Id = Guid.NewGuid(),
            VendaId = venda.Id,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Quantidade = Quantidade.From(2),
            PrecoUnitario = Dinheiro.FromDecimal(75m),
            PrecoTotal = Dinheiro.FromDecimal(150m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.ItensVenda.Add(itemVenda);

        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = Quantidade.From(2),
            ValorUnitario = Dinheiro.FromDecimal(75m),
            ValorTotal = Dinheiro.FromDecimal(150m),
            DataMovimentacao = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow
        };
        dbContext.MovimentacoesEstoque.Add(movimentacao);

        await dbContext.SaveChangesAsync();

        // Act
        var result = await repo.GetDashboardResumoAsync(empresaId, 30);

        // Assert
        result.Should().NotBeNull();
        result.TotalSkus.Should().Be(1);
        result.QuantidadeTotalEmEstoque.Should().Be(10);
    }

    [SkippableFact] // BUG-05 (QA v1.10 #674): contagem "estoque critico" do dashboard == filtro /estoque?status=critico.
    public async Task DashboardEstoqueCritico_BateComFiltroCritico_IncluindoEsgotados()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);
        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Cat", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoriaId, Nome = "P", Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow };
        dbContext.Produtos.Add(produto);

        ItemEstoque Lote(StatusItemEstoque status, int qtd) => new()
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(qtd), QuantidadeInicial = Quantidade.From(qtd),
            CustoUnitario = Dinheiro.FromDecimal(10m), PrecoVendaSugerido = Dinheiro.FromDecimal(15m),
            EntradaEm = DateTime.UtcNow, Status = status, CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow
        };
        // 2 Critical + 2 Esgotado(qty0) = 4 "precisa repor"; 1 Vencido + 1 Ok ficam de fora.
        // Reproduz o cenario do QA (6 no dashboard vs 4 na lista): aqui ambos devem dar 4.
        dbContext.ItensEstoque.AddRange(
            Lote(StatusItemEstoque.Critical, 1),
            Lote(StatusItemEstoque.Critical, 2),
            Lote(StatusItemEstoque.Esgotado, 0),
            Lote(StatusItemEstoque.Esgotado, 0),
            Lote(StatusItemEstoque.Vencido, 5),
            Lote(StatusItemEstoque.Ok, 50));
        await dbContext.SaveChangesAsync();

        var analytics = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);
        var estoqueRepo = new Infra.Postgre.Repositories.ItemEstoqueRepository(dbContext);

        var resumo = await analytics.GetDashboardResumoAsync(empresaId, 30);
        var (_, totalCritico) = await estoqueRepo.GetItensEstoquePaginadosAsync(empresaId, 1, 50, status: "critico");

        resumo.AlertasEstoqueBaixo.Should().Be(4, "dashboard conta Critical + Esgotado");
        totalCritico.Should().Be(4, "filtro 'critico' passa a incluir Esgotado");
        resumo.AlertasEstoqueBaixo.Should().Be(totalCritico, "dashboard e filtro usam a MESMA definicao (BUG-05)");
    }

    [SkippableFact]
    public async Task GetMargemPorProdutoAsync_DeveRetornarMargensCorretas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            PrecoVendaSugerido = Dinheiro.FromDecimal(75m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = Quantidade.From(1),
            ValorUnitario = Dinheiro.FromDecimal(75m),
            ValorTotal = Dinheiro.FromDecimal(75m),
            DataMovimentacao = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow
        };
        dbContext.MovimentacoesEstoque.Add(movimentacao);

        await dbContext.SaveChangesAsync();

        // Act
        var items = await repo.GetMargemPorProdutoAsync(empresaId, 30);

        // Assert
        items.Should().NotBeNull();
        items.Should().HaveCount(1);
        var margem = items.First();
        margem.ProdutoId.Should().Be(produto.Id);
        margem.NomeProduto.Should().Be("Produto Teste");
    }

    [SkippableFact]
    public async Task GetReceitaPorPeriodoAsync_DeveRetornarReceitasCorretas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Receita",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU-REC"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Canal = CanalVenda.LojaPropria,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            DataVenda = DateTime.UtcNow,
            ValorTotal = Dinheiro.FromDecimal(100m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.Vendas.Add(venda);

        var itemVenda = new ItemVenda
        {
            Id = Guid.NewGuid(),
            VendaId = venda.Id,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Quantidade = Quantidade.From(1),
            PrecoUnitario = Dinheiro.FromDecimal(100m),
            PrecoTotal = Dinheiro.FromDecimal(100m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.ItensVenda.Add(itemVenda);

        await dbContext.SaveChangesAsync();

        // Act
        var result = await repo.GetReceitaPorPeriodoAsync(empresaId, 12);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
    }

    [SkippableFact]
    public async Task GetMovimentacoesResumoAsync_DeveRetornarMovimentacoesCorretas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            PrecoVendaSugerido = Dinheiro.FromDecimal(75m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = Quantidade.From(2),
            ValorUnitario = Dinheiro.FromDecimal(75m),
            ValorTotal = Dinheiro.FromDecimal(150m),
            DataMovimentacao = DateTime.UtcNow,
            CriadoEm = DateTime.UtcNow
        };
        dbContext.MovimentacoesEstoque.Add(movimentacao);

        await dbContext.SaveChangesAsync();

        // Act
        var result = await repo.GetMovimentacoesResumoAsync(empresaId, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
    }

    [SkippableFact]
    public async Task GetAlertasValidadeAsync_DeveRetornarAlertasCorretos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(5),
            QuantidadeInicial = Quantidade.From(5),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            PrecoVendaSugerido = Dinheiro.FromDecimal(75m),
            EntradaEm = DateTime.UtcNow,
            ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(15)),
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        await dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await repo.GetAlertasValidadeAsync(empresaId, 30);

        // Assert
        items.Should().NotBeNull();
        totalCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task GetItensParadosDetalhadosAsync_DeveRetornarItensParadosCorretos()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            PrecoVendaSugerido = Dinheiro.FromDecimal(75m),
            EntradaEm = DateTime.UtcNow.AddDays(-100),
            UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-100),
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        await dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await repo.GetItensParadosDetalhadosAsync(empresaId, 90);

        // Assert
        items.Should().NotBeNull();
        totalCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task GetSugestaoReposicaoDetalhadaAsync_DeveRetornarSugestoesCorretas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(2),
            QuantidadeInicial = Quantidade.From(10),
            QuantidadeMinima = 5,
            CustoUnitario = Dinheiro.FromDecimal(50m),
            PrecoVendaSugerido = Dinheiro.FromDecimal(75m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        await dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await repo.GetSugestaoReposicaoDetalhadaAsync(empresaId, 30);

        // Assert
        items.Should().NotBeNull();
        totalCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task GetProjecaoRupturaAsync_DeveRetornarProjecoesCorretas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            PrecoVendaSugerido = Dinheiro.FromDecimal(75m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        await dbContext.SaveChangesAsync();

        // Act
        var (items, totalCount) = await repo.GetProjecaoRupturaAsync(empresaId, 30);

        // Assert
        items.Should().NotBeNull();
        totalCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task GetVendasPorCanalAsync_DeveRetornarVendasPorCanalCorretas()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N")[..14], CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
        dbContext.Categorias.Add(new Categoria { Id = categoriaId, EmpresaId = empresaId, Nome = "Categoria Teste", CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            CategoriaId = categoriaId,
            Nome = "Produto Canal",
            Tipo = TipoProduto.Fisico,
            SkuBase = CodigoSku.From("SKU-CANAL"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeInicial = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Canal = CanalVenda.LojaPropria,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            DataVenda = DateTime.UtcNow,
            ValorTotal = Dinheiro.FromDecimal(100m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.Vendas.Add(venda);

        var itemVenda = new ItemVenda
        {
            Id = Guid.NewGuid(),
            VendaId = venda.Id,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Quantidade = Quantidade.From(1),
            PrecoUnitario = Dinheiro.FromDecimal(100m),
            PrecoTotal = Dinheiro.FromDecimal(100m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.ItensVenda.Add(itemVenda);

        await dbContext.SaveChangesAsync();

        // Act
        var result = await repo.GetVendasPorCanalAsync(empresaId, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCountGreaterThan(0);
    }
}
