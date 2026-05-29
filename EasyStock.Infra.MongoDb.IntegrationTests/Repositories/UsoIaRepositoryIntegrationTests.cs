using EasyStock.Domain.Entities;
using EasyStock.Infra.MongoDb.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.IntegrationTests.Repositories;

[Collection("MongoDbTestCollection")]
public sealed class UsoIaRepositoryIntegrationTests(MongoDbFixture fixture)
{
    [Fact]
    public async Task GetAsync_DeveRetornarUsoCorreto()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
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
            TotalTokens = 1000,
            AtualizadoEm = DateTime.UtcNow
        };
        await context.GetCollection<UsoIa>("uso_ia").InsertOneAsync(uso);

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
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
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
            TotalTokens = 500,
            AtualizadoEm = DateTime.UtcNow
        };

        // Act
        await repo.AddAsync(uso);
        await unitOfWork.CommitAsync();

        // Assert
        var saved = await context.GetCollection<UsoIa>("uso_ia").Find(x => x.Id == uso.Id).FirstOrDefaultAsync();
        saved.Should().NotBeNull();
        saved!.TotalTokens.Should().Be(500);
    }

    [Fact]
    public async Task UpdateAsync_DeveAtualizarUso()
    {
        // Arrange
        if (!fixture.IsAvailable) return;
        await using var services = fixture.CreateServiceProvider();
        using var scope = services.CreateScope();
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
            TotalTokens = 200,
            AtualizadoEm = DateTime.UtcNow
        };
        await context.GetCollection<UsoIa>("uso_ia").InsertOneAsync(uso);

        // Act
        uso.TotalTokens = 300;
        await repo.UpdateAsync(uso);
        await unitOfWork.CommitAsync();

        // Assert
        var updated = await context.GetCollection<UsoIa>("uso_ia").Find(x => x.Id == uso.Id).FirstOrDefaultAsync();
        updated!.TotalTokens.Should().Be(300);
    }
}