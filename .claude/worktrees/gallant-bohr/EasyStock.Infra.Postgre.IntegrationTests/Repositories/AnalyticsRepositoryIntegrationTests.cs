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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar produto
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = new CodigoSku("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        // Criar item de estoque
        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(10),
            QuantidadeInicial = new Quantidade(10),
            CustoUnitario = new Dinheiro(50m),
            PrecoVendaSugerido = new Dinheiro(75m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        // Criar venda
        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Canal = CanalVenda.Online,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            DataVenda = DateTime.UtcNow,
            ValorTotal = new Dinheiro(150m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.Vendas.Add(venda);

        // Criar item venda
        var itemVenda = new ItemVenda
        {
            Id = Guid.NewGuid(),
            VendaId = venda.Id,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Quantidade = new Quantidade(2),
            PrecoUnitario = new Dinheiro(75m),
            PrecoTotal = new Dinheiro(150m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.ItensVenda.Add(itemVenda);

        // Criar movimentacao
        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = new Quantidade(2),
            ValorUnitario = new Dinheiro(75m),
            ValorTotal = new Dinheiro(150m),
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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar produto
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = new CodigoSku("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        // Criar item de estoque
        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(10),
            QuantidadeInicial = new Quantidade(10),
            CustoUnitario = new Dinheiro(50m),
            PrecoVendaSugerido = new Dinheiro(75m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        // Criar movimentacao
        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = new Quantidade(1),
            ValorUnitario = new Dinheiro(75m),
            ValorTotal = new Dinheiro(75m),
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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar venda
        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Canal = CanalVenda.Online,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            DataVenda = DateTime.UtcNow,
            ValorTotal = new Dinheiro(100m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.Vendas.Add(venda);

        // Criar item venda
        var itemVenda = new ItemVenda
        {
            Id = Guid.NewGuid(),
            VendaId = venda.Id,
            ItemEstoqueId = Guid.NewGuid(), // dummy
            ProdutoId = Guid.NewGuid(), // dummy
            Quantidade = new Quantidade(1),
            PrecoUnitario = new Dinheiro(100m),
            PrecoTotal = new Dinheiro(100m),
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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar produto
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = new CodigoSku("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        // Criar item de estoque
        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(10),
            QuantidadeInicial = new Quantidade(10),
            CustoUnitario = new Dinheiro(50m),
            PrecoVendaSugerido = new Dinheiro(75m),
            EntradaEm = DateTime.UtcNow,
            Status = StatusItemEstoque.Ok,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.ItensEstoque.Add(itemEstoque);

        // Criar movimentacao
        var movimentacao = new MovimentacaoEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ItemEstoqueId = itemEstoque.Id,
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacaoEstoque.Saida,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            Quantidade = new Quantidade(2),
            ValorUnitario = new Dinheiro(75m),
            ValorTotal = new Dinheiro(150m),
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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar produto
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = new CodigoSku("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        // Criar item de estoque
        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(5),
            QuantidadeInicial = new Quantidade(5),
            CustoUnitario = new Dinheiro(50m),
            PrecoVendaSugerido = new Dinheiro(75m),
            EntradaEm = DateTime.UtcNow,
            ValidadeEm = new Validade(DateTime.UtcNow.AddDays(15)),
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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar produto
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = new CodigoSku("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        // Criar item de estoque
        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(10),
            QuantidadeInicial = new Quantidade(10),
            CustoUnitario = new Dinheiro(50m),
            PrecoVendaSugerido = new Dinheiro(75m),
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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar produto
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = new CodigoSku("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        // Criar item de estoque com quantidade baixa
        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(2),
            QuantidadeInicial = new Quantidade(10),
            QuantidadeMinima = 5,
            CustoUnitario = new Dinheiro(50m),
            PrecoVendaSugerido = new Dinheiro(75m),
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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar produto
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Produto Teste",
            Tipo = TipoProduto.Fisico,
            SkuBase = new CodigoSku("SKU123"),
            Status = StatusProduto.Ativo,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        dbContext.Produtos.Add(produto);

        // Criar item de estoque
        var itemEstoque = new ItemEstoque
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produto.Id,
            QuantidadeAtual = new Quantidade(10),
            QuantidadeInicial = new Quantidade(10),
            CustoUnitario = new Dinheiro(50m),
            PrecoVendaSugerido = new Dinheiro(75m),
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
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnalyticsRepository(dbContext);

        var empresaId = Guid.NewGuid();

        // Criar venda
        var venda = new Venda
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Canal = CanalVenda.Online,
            Natureza = NaturezaMovimentacaoEstoque.Venda,
            DataVenda = DateTime.UtcNow,
            ValorTotal = new Dinheiro(100m),
            CriadoEm = DateTime.UtcNow
        };
        dbContext.Vendas.Add(venda);

        // Criar item venda
        var itemVenda = new ItemVenda
        {
            Id = Guid.NewGuid(),
            VendaId = venda.Id,
            ItemEstoqueId = Guid.NewGuid(), // dummy
            ProdutoId = Guid.NewGuid(), // dummy
            Quantidade = new Quantidade(1),
            PrecoUnitario = new Dinheiro(100m),
            PrecoTotal = new Dinheiro(100m),
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
