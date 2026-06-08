using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Faturas;

/// <summary>
/// Unit (SEM Docker) do guard-rail do <c>FaturaRepository.UpdateAsync</c>: chamar
/// com uma fatura DETACHED deve falhar rapido (fail-fast) em vez de re-anexar o
/// grafo e reintroduzir o BUG-01 (filho novo rebaixado a Modified). Roda sempre —
/// <c>db.Entry().State</c> nao faz I/O, entao nao depende de Postgres.
/// </summary>
public sealed class FaturaRepositoryFailFastTests
{
    [Fact]
    public async Task UpdateAsync_com_fatura_detached_lanca_InvalidOperationException()
    {
        var options = new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none")
            .Options;
        await using var db = new EasyStockDbContext(options);
        var repo = new FaturaRepository(db);

        var detached = Fatura.Criar(
            Guid.NewGuid(), "2026-000001",
            new DadosFaturado("X"), new DadosEmissor("Y"),
            OrigemFatura.Avulsa, DateTime.UtcNow, DateTime.UtcNow.AddDays(1));

        var act = async () => await repo.UpdateAsync(detached);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "UpdateAsync nao deve aceitar entidade detached — carregue via GetByIdAsync e confie no change tracker");
    }
}
