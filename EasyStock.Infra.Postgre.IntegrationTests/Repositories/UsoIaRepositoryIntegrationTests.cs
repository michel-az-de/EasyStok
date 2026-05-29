using EasyStock.Domain.Entities;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

[Collection("PostgreSqlTestCollection")]
public sealed class UsoIaRepositoryIntegrationTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task GetAsync_DeveRetornarUsoCorreto()
    {
        // Arrange
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.UsoIaRepository(dbContext);

        var empresaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);
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

    [SkippableFact]
    public async Task AddAsync_DeveAdicionarUso()
    {
        // Arrange
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.UsoIaRepository(dbContext);

        var empresaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);
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

    [SkippableFact]
    public async Task UpdateAsync_DeveAtualizarUso()
    {
        // Arrange
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var dbContext = fixture.CreateDbContext();
        var repo = new Infra.Postgre.Repositories.UsoIaRepository(dbContext);

        var empresaId = Guid.NewGuid();
        dbContext.SetMobileTenantContext(empresaId);
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
