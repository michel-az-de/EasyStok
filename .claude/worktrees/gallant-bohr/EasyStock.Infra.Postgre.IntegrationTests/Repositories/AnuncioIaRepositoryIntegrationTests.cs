using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.IntegrationTests;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class AnuncioIaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    private readonly IServiceProvider _services = fixture.Services;

    [Fact]
    public async Task GetByIdAsync_DeveRetornarAnuncioCorreto()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        dbContext.AnunciosIa.Add(anuncio);
        await dbContext.SaveChangesAsync();

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
        var dbContext = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        dbContext.AnunciosIa.AddRange(anuncio1, anuncio2);
        await dbContext.SaveChangesAsync();

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
        var dbContext = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        await dbContext.SaveChangesAsync();

        // Assert
        var saved = await dbContext.AnunciosIa.FindAsync(anuncio.Id);
        saved.Should().NotBeNull();
        saved!.DescricaoGerada.Should().Be("Nova descrição");
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarAnuncio()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        dbContext.AnunciosIa.Add(anuncio);
        await dbContext.SaveChangesAsync();

        // Act
        anuncio.DescricaoGerada = "Descrição atualizada";
        await repo.UpdateAsync(anuncio);
        await dbContext.SaveChangesAsync();

        // Assert
        var updated = await dbContext.AnunciosIa.FindAsync(anuncio.Id);
        updated!.DescricaoGerada.Should().Be("Descrição atualizada");
    }

    [Fact]
    public async Task RemoveAsync_DeveRemoverAnuncio()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        dbContext.AnunciosIa.Add(anuncio);
        await dbContext.SaveChangesAsync();

        // Act
        await repo.RemoveAsync(anuncio);
        await dbContext.SaveChangesAsync();

        // Assert
        var removed = await dbContext.AnunciosIa.FindAsync(anuncio.Id);
        removed.Should().BeNull();
    }
}