using EasyStock.Domain.Entities;
using EasyStock.Infra.MongoDb.Data;
using EasyStock.Infra.MongoDb.IntegrationTests;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace EasyStock.Infra.MongoDb.IntegrationTests.Repositories;

[Collection("MongoDbTestCollection")]
public sealed class UsoIaRepositoryIntegrationTests(MongoDbFixture fixture)
{
    private readonly IServiceProvider _services = fixture.Services;

    [Fact]
    public async Task GetAsync_DeveRetornarUsoCorreto()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var repo = new Infra.MongoDb.Repositories.UsoIaRepository(context, unitOfWork);

        var empresaId = Guid.NewGuid();
        var ano = 2024;
        var mes = 4;

        var uso = new UsoIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Ano = ano,
            Mes = mes,
            TokensUsados = 1000,
            Custos = 0.5m,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<UsoIa>(MongoCollectionNames.UsoIa).InsertOneAsync(uso);

        // Act
        var result = await repo.GetAsync(empresaId, ano, mes);

        // Assert
        result.Should().NotBeNull();
        result!.TokensUsados.Should().Be(1000);
    }

    [Fact]
    public async Task AddAsync_DeveAdicionarUso()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var repo = new Infra.MongoDb.Repositories.UsoIaRepository(context, unitOfWork);

        var empresaId = Guid.NewGuid();
        var ano = 2024;
        var mes = 5;

        var uso = new UsoIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Ano = ano,
            Mes = mes,
            TokensUsados = 500,
            Custos = 0.25m,
            CriadoEm = DateTime.UtcNow
        };

        // Act
        await repo.AddAsync(uso);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var saved = await context.GetCollection<UsoIa>(MongoCollectionNames.UsoIa).Find(x => x.Id == uso.Id).FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.TokensUsados.Should().Be(500);
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarUso()
    {
        // Arrange
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MongoEasyStockContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<MongoUnitOfWork>();
        var repo = new Infra.MongoDb.Repositories.UsoIaRepository(context, unitOfWork);

        var empresaId = Guid.NewGuid();
        var ano = 2024;
        var mes = 6;

        var uso = new UsoIa
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Ano = ano,
            Mes = mes,
            TokensUsados = 200,
            Custos = 0.1m,
            CriadoEm = DateTime.UtcNow
        };
        await context.GetCollection<UsoIa>(MongoCollectionNames.UsoIa).InsertOneAsync(uso);

        // Act
        uso.TokensUsados = 300;
        await repo.UpdateAsync(uso);
        await unitOfWork.SaveChangesAsync();

        // Assert
        var updated = await context.GetCollection<UsoIa>(MongoCollectionNames.UsoIa).Find(x => x.Id == uso.Id).FirstOrDefaultAsync();
        updated!.TokensUsados.Should().Be(300);
    }
}