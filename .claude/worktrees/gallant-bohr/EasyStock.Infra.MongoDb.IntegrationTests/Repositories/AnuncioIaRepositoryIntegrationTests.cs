using EasyStock.Domain.Entities;
using EasyStock.Infra.MongoDb.Data;
using EasyStock.Infra.MongoDb.IntegrationTests;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace EasyStock.Infra.MongoDb.IntegrationTests.Repositories;

[Collection("MongoDbTestCollection")]
public sealed class AnuncioIaRepositoryIntegrationTests(MongoDbFixture fixture)
{
    private readonly IServiceProvider _services = fixture.Services;

    [Fact]
    public async Task GetByIdAsync_DeveRetornarAnuncioCorreto()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var repo = new Infra.MongoDb.Repositories.AnuncioIaRepository(context, unitOfWork);

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var anuncio = new AnuncioIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            DescricaoGerada = "Descrição teste",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<AnuncioIa>(MongoCollectionNames.AnunciosIa).InsertOneAsync(anuncio);

        // Act
        var result = await repo.GetByIdAsync(empresaId, anuncio.Id);

        // Assert
        result.Should().NotBeNull();
        result!.DescricaoGerada.Should().Be("Descrição teste");
    }

    [Fact]
    public async Task GetByProdutoAsync_DeveRetornarAnunciosDoProduto()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var repo = new Infra.MongoDb.Repositories.AnuncioIaRepository(context, unitOfWork);

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var anuncio1 = new AnuncioIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            DescricaoGerada = "Descrição 1",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };
        var anuncio2 = new AnuncioIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            DescricaoGerada = "Descrição 2",
            Salvo = false,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<AnuncioIa>(MongoCollectionNames.AnunciosIa).InsertManyAsync([anuncio1, anuncio2]);

        // Act
        var result = await repo.GetByProdutoAsync(empresaId, produtoId);

        // Assert
        result.Should().HaveCount(1);
        result.First().DescricaoGerada.Should().Be("Descrição 1");
    }

    [Fact]
    public async Task AddAsync_DeveAdicionarAnuncio()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var repo = new Infra.MongoDb.Repositories.AnuncioIaRepository(context, unitOfWork);

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var anuncio = new AnuncioIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            DescricaoGerada = "Nova descrição",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };

        // Act
        await repo.AddAsync(anuncio);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var saved = await context.GetCollection<AnuncioIa>(MongoCollectionNames.AnunciosIa).Find(x => x.Id == anuncio.Id).FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.DescricaoGerada.Should().Be("Nova descrição");
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarAnuncio()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var repo = new Infra.MongoDb.Repositories.AnuncioIaRepository(context, unitOfWork);

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var anuncio = new AnuncioIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            DescricaoGerada = "Descrição original",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<AnuncioIa>(MongoCollectionNames.AnunciosIa).InsertOneAsync(anuncio);

        // Act
        anuncio.DescricaoGerada = "Descrição atualizada";
        await repo.UpdateAsync(anuncio);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var updated = await context.GetCollection<AnuncioIa>(MongoCollectionNames.AnunciosIa).Find(x => x.Id == anuncio.Id).FirstOrDefaultAsync();
        updated!.DescricaoGerada.Should().Be("Descrição atualizada");
    }

    [Fact]
    public async Task RemoveAsync_DeveRemoverAnuncio()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var repo = new Infra.MongoDb.Repositories.AnuncioIaRepository(context, unitOfWork);

        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var anuncio = new AnuncioIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            DescricaoGerada = "Descrição para remover",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<AnuncioIa>(MongoCollectionNames.AnunciosIa).InsertOneAsync(anuncio);

        // Act
        await repo.RemoveAsync(anuncio);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var removed = await context.GetCollection<AnuncioIa>(MongoCollectionNames.AnunciosIa).Find(x => x.Id == anuncio.Id).FirstOrDefaultAsync();
        removed.Should().BeNull();
    }
}