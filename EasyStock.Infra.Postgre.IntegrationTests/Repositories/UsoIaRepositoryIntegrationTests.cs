using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.IntegrationTests;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class UsoIaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [Fact]
    public async Task GetAsync_DeveRetornarUsoCorreto()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.UsoIaRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var ano = 2024;
        var mes = 4;

        var uso = new UsoIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Ano = ano,
            Mes = mes,
            TotalTokens = 1000,
            AtualizadoEm = DateTime.UtcNow
        };
        dbContext.UsoIa.Add(uso);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await repo.GetAsync(empresaId, ano, mes);

        // Assert
        result.Should().NotBeNull();
        result!.TotalTokens.Should().Be(1000);
    }

    [Fact]
    public async Task AddAsync_DeveAdicionarUso()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.UsoIaRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var ano = 2024;
        var mes = 5;

        var uso = new UsoIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Ano = ano,
            Mes = mes,
            TotalTokens = 500,
            AtualizadoEm = DateTime.UtcNow
        };

        // Act
        await repo.AddAsync(uso);
        await dbContext.SaveChangesAsync();

        // Assert
        var saved = await dbContext.UsoIa.FindAsync(uso.Id);
        saved.Should().NotBeNull();
        saved!.TotalTokens.Should().Be(500);
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarUso()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.UsoIaRepository(dbContext);

        var empresaId = Guid.NewGuid();
        var ano = 2024;
        var mes = 6;

        var uso = new UsoIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Ano = ano,
            Mes = mes,
            TotalTokens = 200,
            AtualizadoEm = DateTime.UtcNow
        };
        dbContext.UsoIa.Add(uso);
        await dbContext.SaveChangesAsync();

        // Act
        uso.TotalTokens = 300;
        await repo.UpdateAsync(uso);
        await dbContext.SaveChangesAsync();

        // Assert
        var updated = await dbContext.UsoIa.FindAsync(uso.Id);
        updated!.TotalTokens.Should().Be(300);
    }
}
