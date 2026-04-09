using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.IntegrationTests;
using FluentAssertions;
using Xunit;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class AnalyticsRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [Fact]
    public async Task GetDashboardResumoAsync_DeveRetornarResumoCorreto()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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

    [Fact]
    public async Task GetMargemPorProdutoAsync_DeveRetornarMargensCorretas()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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

    [Fact]
    public async Task GetReceitaPorPeriodoAsync_DeveRetornarReceitasCorretas()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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

    [Fact]
    public async Task GetMovimentacoesResumoAsync_DeveRetornarMovimentacoesCorretas()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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

    [Fact]
    public async Task GetAlertasValidadeAsync_DeveRetornarAlertasCorretos()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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

    [Fact]
    public async Task GetItensParadosDetalhadosAsync_DeveRetornarItensParadosCorretos()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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

    [Fact]
    public async Task GetSugestaoReposicaoDetalhadaAsync_DeveRetornarSugestoesCorretas()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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

    [Fact]
    public async Task GetProjecaoRupturaAsync_DeveRetornarProjecoesCorretas()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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

    [Fact]
    public async Task GetVendasPorCanalAsync_DeveRetornarVendasPorCanalCorretas()
    {
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var categoriaId = Guid.NewGuid();

        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Teste", Documento = empresaId.ToString("N"), CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow });
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
