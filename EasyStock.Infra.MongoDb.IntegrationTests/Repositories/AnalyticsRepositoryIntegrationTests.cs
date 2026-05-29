using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.MongoDb.Data;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.IntegrationTests.Repositories;

[Collection("MongoDbTestCollection")]
public sealed class AnalyticsRepositoryIntegrationTests(MongoDbFixture fixture)
{
    [Fact]
    public async Task GetDashboardResumoAsync_DeveRetornarResumoCorreto()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>("produtos").InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            QuantidadeMinima = 5,
            CustoUnitario = Dinheiro.FromDecimal(50m),
            PrecoVendaSugerido = Dinheiro.FromDecimal(75m)
        };
        await context.GetCollection<ItemEstoque>("itens_estoque").InsertOneAsync(itemEstoque);

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            DataVenda = DateTime.UtcNow,
            ValorTotal = Dinheiro.FromDecimal(150m),
            ItensVenda = new List<ItemVenda>
            {
                new ItemVenda
                {
                    Id = Guid.NewGuid(),
                    ProdutoId = produto.Id,
                    Quantidade = Quantidade.From(2),
                    PrecoUnitario = Dinheiro.FromDecimal(75m),
                    PrecoTotal = Dinheiro.FromDecimal(150m)
                }
            }
        };
        await context.GetCollection<Venda>("vendas").InsertOneAsync(venda);

        // Act
        var result = await repo.GetDashboardResumoAsync(empresaId, 30);

        // Assert
        result.EmpresaId.Should().Be(empresaId);
        result.TotalSkus.Should().BeGreaterThanOrEqualTo(1);
        result.QuantidadeTotalEmEstoque.Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task GetReceitaPorPeriodoAsync_DeveRetornarReceitasAgrupadas()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            DataVenda = DateTime.UtcNow,
            ValorTotal = Dinheiro.FromDecimal(100m),
            ItensVenda = new List<ItemVenda>
            {
                new ItemVenda
                {
                    Id = Guid.NewGuid(),
                    Quantidade = Quantidade.From(1),
                    PrecoUnitario = Dinheiro.FromDecimal(100m),
                    PrecoTotal = Dinheiro.FromDecimal(100m)
                }
            }
        };
        await context.GetCollection<Venda>("vendas").InsertOneAsync(venda);

        // Act
        var result = await repo.GetReceitaPorPeriodoAsync(empresaId, 12);

        // Assert
        result.Should().NotBeEmpty();
        result.First().ReceitaBruta.Should().BeGreaterThanOrEqualTo(100m);
    }

    [Fact]
    public async Task GetMargemPorProdutoAsync_DeveRetornarMargens()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>("produtos").InsertOneAsync(produto);

        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            DataMovimentacao = DateTime.UtcNow,
            Quantidade = Quantidade.From(5),
            ValorUnitario = Dinheiro.FromDecimal(60m),
            ValorTotal = Dinheiro.FromDecimal(300m)
        };
        await context.GetCollection<MovimentacaoEstoque>("movimentacoes_estoque").InsertOneAsync(movimentacao);

        // Act
        var items = await repo.GetMargemPorProdutoAsync(empresaId, 30);

        // Assert
        items.Should().NotBeEmpty();
        items.First().NomeProduto.Should().Be("Produto Teste");
    }

    [Fact]
    public async Task GetMovimentacoesResumoAsync_DeveRetornarResumoMovimentacoes()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Tipo = TipoMovimentacaoEstoque.Saida,
            DataMovimentacao = DateTime.UtcNow,
            Quantidade = Quantidade.From(10),
            ValorTotal = Dinheiro.FromDecimal(500m)
        };
        await context.GetCollection<MovimentacaoEstoque>("movimentacoes_estoque").InsertOneAsync(movimentacao);

        // Act
        var result = await repo.GetMovimentacoesResumoAsync(empresaId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        // Assert
        result.Should().NotBeEmpty();
        result.First().QuantidadeTotal.Should().Be(10);
    }

    [Fact]
    public async Task GetAlertasValidadeAsync_DeveRetornarItensComValidadeProxima()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>("produtos").InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(5),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            ValidadeEm = Validade.From(DateTime.UtcNow.AddDays(15))
        };
        await context.GetCollection<ItemEstoque>("itens_estoque").InsertOneAsync(itemEstoque);

        // Act
        var (items, _) = await repo.GetAlertasValidadeAsync(empresaId, 30);

        // Assert
        items.Should().NotBeEmpty();
        items.First().NomeProduto.Should().Be("Produto Teste");
    }

    [Fact]
    public async Task GetItensParadosDetalhadosAsync_DeveRetornarItensSemMovimento()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>("produtos").InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m),
            UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-100)
        };
        await context.GetCollection<ItemEstoque>("itens_estoque").InsertOneAsync(itemEstoque);

        // Act
        var (items, _) = await repo.GetItensParadosDetalhadosAsync(empresaId, 90);

        // Assert
        items.Should().NotBeEmpty();
        items.First().NomeProduto.Should().Be("Produto Teste");
    }

    [Fact]
    public async Task GetSazonalidadeAsync_DeveRetornarSazonalidadeMensal()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            Tipo = TipoMovimentacaoEstoque.Saida,
            DataMovimentacao = DateTime.UtcNow,
            Quantidade = Quantidade.From(20),
            ValorTotal = Dinheiro.FromDecimal(1000m)
        };
        await context.GetCollection<MovimentacaoEstoque>("movimentacoes_estoque").InsertOneAsync(movimentacao);

        // Act
        var result = await repo.GetSazonalidadeAsync(empresaId, produtoId, 12);

        // Assert
        result.Should().NotBeEmpty();
        result.First().TotalSaidas.Should().Be(20);
    }

    [Fact]
    public async Task GetSugestaoReposicaoDetalhadaAsync_DeveRetornarSugestoes()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>("produtos").InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(2),
            QuantidadeMinima = 10,
            CustoUnitario = Dinheiro.FromDecimal(50m)
        };
        await context.GetCollection<ItemEstoque>("itens_estoque").InsertOneAsync(itemEstoque);

        // Act
        var (items, _) = await repo.GetSugestaoReposicaoDetalhadaAsync(empresaId, 30);

        // Assert
        items.Should().NotBeEmpty();
        items.First().NomeProduto.Should().Be("Produto Teste");
    }

    [Fact]
    public async Task GetProjecaoRupturaAsync_DeveRetornarProjecoes()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = CodigoSku.From("SKU123"),
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>("produtos").InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = Quantidade.From(10),
            CustoUnitario = Dinheiro.FromDecimal(50m)
        };
        await context.GetCollection<ItemEstoque>("itens_estoque").InsertOneAsync(itemEstoque);

        // Act
        var (items, _) = await repo.GetProjecaoRupturaAsync(empresaId, 30);

        // Assert
        items.Should().NotBeEmpty();
        items.First().NomeProduto.Should().Be("Produto Teste");
    }

    [Fact]
    public async Task GetVendasPorCanalAsync_DeveRetornarVendasAgrupadasPorCanal()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            DataVenda = DateTime.UtcNow,
            Canal = CanalVenda.MercadoLivre,
            ValorTotal = Dinheiro.FromDecimal(200m),
            ItensVenda = new List<ItemVenda>
            {
                new ItemVenda
                {
                    Id = Guid.NewGuid(),
                    Quantidade = Quantidade.From(2),
                    PrecoUnitario = Dinheiro.FromDecimal(100m),
                    PrecoTotal = Dinheiro.FromDecimal(200m)
                }
            }
        };
        await context.GetCollection<Venda>("vendas").InsertOneAsync(venda);

        // Act
        var result = await repo.GetVendasPorCanalAsync(empresaId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        // Assert
        result.Should().NotBeEmpty();
        result.First().Canal.Should().Be(CanalVenda.MercadoLivre);
    }
}
