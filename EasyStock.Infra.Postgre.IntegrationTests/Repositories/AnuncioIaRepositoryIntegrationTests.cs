using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.IntegrationTests;
using FluentAssertions;
using Xunit;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class AnuncioIaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task GetByIdAsync_DeveRetornarAnuncioCorreto()
    {
        // Arrange
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        dbContext.AnunciosIa.Add(anuncio);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await repo.GetByIdAsync(empresaId, anuncio.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Conteudo.Should().Be("Descrição teste");
    }

    [SkippableFact]
    public async Task GetByProdutoAsync_DeveRetornarAnunciosDoProduto()
    {
        // Arrange
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        dbContext.AnunciosIa.AddRange(anuncio1, anuncio2);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await repo.GetByProdutoAsync(empresaId, produtoId);

        // Assert
        result.Should().HaveCount(1);
        result.First().Conteudo.Should().Be("Descrição 1");
    }

    [SkippableFact]
    public async Task AddAsync_DeveAdicionarAnuncio()
    {
        // Arrange
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        await dbContext.SaveChangesAsync();

        // Assert
        var saved = await dbContext.AnunciosIa.FindAsync(anuncio.Id);
        saved.Should().NotBeNull();
        saved!.Conteudo.Should().Be("Nova descrição");
    }

    [SkippableFact]
    public async Task UpdateAsync_DeveAtualizarAnuncio()
    {
        // Arrange
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
        dbContext.AnunciosIa.Add(anuncio);
        await dbContext.SaveChangesAsync();

        // Act
        anuncio.Conteudo = "Descrição atualizada";
        await repo.UpdateAsync(anuncio);
        await dbContext.SaveChangesAsync();

        // Assert
        var updated = await dbContext.AnunciosIa.FindAsync(anuncio.Id);
        updated!.Conteudo.Should().Be("Descrição atualizada");
    }

    [SkippableFact]
    public async Task RemoveAsync_DeveRemoverAnuncio()
    {
        // Arrange
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.AnuncioIaRepository(dbContext);

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
