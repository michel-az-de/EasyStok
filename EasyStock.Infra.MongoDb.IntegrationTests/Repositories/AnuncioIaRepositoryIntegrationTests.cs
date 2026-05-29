using EasyStock.Domain.Entities;
using EasyStock.Infra.MongoDb.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.IntegrationTests.Repositories;

[Collection("MongoDbTestCollection")]
public sealed class AnuncioIaRepositoryIntegrationTests(MongoDbFixture fixture)
{
    [Fact]
    public async Task GetByIdAsync_DeveRetornarAnuncioCorreto()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
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
            Titulo = "Título teste",
            Conteudo = "Descrição teste",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<AnuncioIa>("anuncios_ia").InsertOneAsync(anuncio);

        // Act
        var result = await repo.GetByIdAsync(empresaId, anuncio.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Conteudo.Should().Be("Descrição teste");
    }

    [Fact]
    public async Task GetByProdutoAsync_DeveRetornarAnunciosDoProduto()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
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
            Titulo = "Título 1",
            Conteudo = "Descrição 1",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };
        var anuncio2 = new AnuncioIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            Titulo = "Título 2",
            Conteudo = "Descrição 2",
            Salvo = false,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<AnuncioIa>("anuncios_ia").InsertManyAsync([anuncio1, anuncio2]);

        // Act
        var result = await repo.GetByProdutoAsync(empresaId, produtoId);

        // Assert
        result.Should().HaveCount(1);
        result.First().Conteudo.Should().Be("Descrição 1");
    }

    [Fact]
    public async Task AddAsync_DeveAdicionarAnuncio()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
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
            Titulo = "Novo título",
            Conteudo = "Nova descrição",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };

        // Act
        await repo.AddAsync(anuncio);
        await unitOfWork.CommitAsync();

        // Assert
        var saved = await context.GetCollection<AnuncioIa>("anuncios_ia").Find(x => x.Id == anuncio.Id).FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.Conteudo.Should().Be("Nova descrição");
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarAnuncio()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
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
            Titulo = "Título original",
            Conteudo = "Descrição original",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<AnuncioIa>("anuncios_ia").InsertOneAsync(anuncio);

        // Act
        anuncio.Conteudo = "Descrição atualizada";
        await repo.UpdateAsync(anuncio);
        await unitOfWork.CommitAsync();

        // Assert
        var updated = await context.GetCollection<AnuncioIa>("anuncios_ia").Find(x => x.Id == anuncio.Id).FirstOrDefaultAsync();
        updated!.Conteudo.Should().Be("Descrição atualizada");
    }

    [Fact]
    public async Task RemoveAsync_DeveRemoverAnuncio()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
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
            Titulo = "Título para remover",
            Conteudo = "Descrição para remover",
            Salvo = true,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<AnuncioIa>("anuncios_ia").InsertOneAsync(anuncio);

        // Act
        await repo.RemoveAsync(anuncio);
        await unitOfWork.CommitAsync();

        // Assert
        var removed = await context.GetCollection<AnuncioIa>("anuncios_ia").Find(x => x.Id == anuncio.Id).FirstOrDefaultAsync();
        removed.Should().BeNull();
    }
}
