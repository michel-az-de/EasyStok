using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.MongoDb.Data;
using EasyStock.Infra.MongoDb.IntegrationTests;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace EasyStock.Infra.MongoDb.IntegrationTests.Repositories;

[Collection("MongoDbTestCollection")]
public sealed class AnalyticsRepositoryIntegrationTests(MongoDbFixture fixture)
{
    private readonly IServiceProvider _services = fixture.Services;

    [Fact]
    public async Task GetDashboardResumoAsync_DeveRetornarResumoCorreto()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        // Adicionar dados de teste
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = "SKU123",
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>(MongoCollectionNames.Produtos).InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(10),
            QuantidadeMinima = 5,
            CustoUnitario = new Dinheiro(50m),
            PrecoVendaSugerido = new Dinheiro(75m)
        };
        await context.GetCollection<ItemEstoque>(MongoCollectionNames.ItensEstoque).InsertOneAsync(itemEstoque);

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            DataVenda = DateTime.UtcNow,
            ValorTotal = new Dinheiro(150m),
            ItensVenda = new List<ItemVenda>
            {
                new ItemVenda
                {
                    Id = Guid.NewGuid(),
                    ProdutoId = produto.Id,
                    Quantidade = new Quantidade(2),
                    ValorUnitario = new Dinheiro(75m)
                }
            }
        };
        await context.GetCollection<Venda>(MongoCollectionNames.Vendas).InsertOneAsync(venda);

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
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            DataVenda = DateTime.UtcNow,
            ValorTotal = new Dinheiro(100m),
            ItensVenda = new List<ItemVenda>
            {
                new ItemVenda
                {
                    Id = Guid.NewGuid(),
                    Quantidade = new Quantidade(1)
                }
            }
        };
        await context.GetCollection<Venda>(MongoCollectionNames.Vendas).InsertOneAsync(venda);

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
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = "SKU123",
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>(MongoCollectionNames.Produtos).InsertOneAsync(produto);

        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            DataMovimentacao = DateTime.UtcNow,
            Quantidade = new Quantidade(5),
            ValorUnitario = new Dinheiro(60m),
            ValorTotal = new Dinheiro(300m)
        };
        await context.GetCollection<MovimentacaoEstoque>(MongoCollectionNames.MovimentacoesEstoque).InsertOneAsync(movimentacao);

        // Act
        var (items, _) = await repo.GetMargemPorProdutoAsync(empresaId, 30);

        // Assert
        items.Should().NotBeEmpty();
        items.First().NomeProduto.Should().Be("Produto Teste");
    }

    [Fact]
    public async Task GetMovimentacoesResumoAsync_DeveRetornarResumoMovimentacoes()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Tipo = TipoMovimentacaoEstoque.Saida,
            DataMovimentacao = DateTime.UtcNow,
            Quantidade = new Quantidade(10),
            ValorTotal = new Dinheiro(500m)
        };
        await context.GetCollection<MovimentacaoEstoque>(MongoCollectionNames.MovimentacoesEstoque).InsertOneAsync(movimentacao);

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
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = "SKU123",
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>(MongoCollectionNames.Produtos).InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(5),
            CustoUnitario = new Dinheiro(50m),
            ValidadeEm = new Validade(DateTime.UtcNow.AddDays(15))
        };
        await context.GetCollection<ItemEstoque>(MongoCollectionNames.ItensEstoque).InsertOneAsync(itemEstoque);

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
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = "SKU123",
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>(MongoCollectionNames.Produtos).InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(10),
            CustoUnitario = new Dinheiro(50m),
            UltimaMovimentacaoEm = DateTime.UtcNow.AddDays(-100)
        };
        await context.GetCollection<ItemEstoque>(MongoCollectionNames.ItensEstoque).InsertOneAsync(itemEstoque);

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
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
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
            Quantidade = new Quantidade(20),
            ValorTotal = new Dinheiro(1000m)
        };
        await context.GetCollection<MovimentacaoEstoque>(MongoCollectionNames.MovimentacoesEstoque).InsertOneAsync(movimentacao);

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
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = "SKU123",
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>(MongoCollectionNames.Produtos).InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(2),
            QuantidadeMinima = 10,
            CustoUnitario = new Dinheiro(50m)
        };
        await context.GetCollection<ItemEstoque>(MongoCollectionNames.ItensEstoque).InsertOneAsync(itemEstoque);

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
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            SkuBase = "SKU123",
            Status = StatusProduto.Ativo
        };
        await context.GetCollection<Produto>(MongoCollectionNames.Produtos).InsertOneAsync(produto);

        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(10),
            CustoUnitario = new Dinheiro(50m)
        };
        await context.GetCollection<ItemEstoque>(MongoCollectionNames.ItensEstoque).InsertOneAsync(itemEstoque);

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
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var repo = new Infra.MongoDb.Repositories.AnalyticsRepository(context, cache);

        var empresaId = Guid.NewGuid();

        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            DataVenda = DateTime.UtcNow,
            Canal = CanalVenda.Marketplace,
            ValorTotal = new Dinheiro(200m),
            ItensVenda = new List<ItemVenda>
            {
                new ItemVenda
                {
                    Id = Guid.NewGuid(),
                    Quantidade = new Quantidade(2)
                }
            }
        };
        await context.GetCollection<Venda>(MongoCollectionNames.Vendas).InsertOneAsync(venda);

        // Act
        var result = await repo.GetVendasPorCanalAsync(empresaId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        // Assert
        result.Should().NotBeEmpty();
        result.First().Canal.Should().Be(CanalVenda.Marketplace);
    }
}