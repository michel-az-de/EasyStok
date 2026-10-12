using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Storefront;

[Collection("PostgreSqlTestCollection")]
public sealed class CheckoutIdempotencyConcorrenciaTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apenas_uma_requisicao_reserva_mesma_chave_mesmo_com_hash_diferente(bool hashesDiferentes)
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL indisponível");
        var key = Guid.NewGuid();
        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = fixture.CreateDbContext();
            var hash = new string(hashesDiferentes && i % 2 == 0 ? 'a' : 'b', 64);
            return await new CheckoutIdempotencyRepository(db).TentarReservarAsync(CheckoutIdempotency.Criar(key, hash));
        }));
        resultados.Count(r => r.reservado).Should().Be(1);
        resultados.Select(r => r.registro.Id).Distinct().Should().ContainSingle();
        await using var verificar = fixture.CreateDbContext();
        (await verificar.CheckoutsIdempotency.CountAsync(r => r.Key == key)).Should().Be(1);
    }
}
