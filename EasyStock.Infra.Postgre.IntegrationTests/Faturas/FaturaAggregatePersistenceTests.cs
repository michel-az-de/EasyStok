using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Faturas;

/// <summary>
/// Fatura como 1a implementacao do <see cref="AggregatePersistenceContract{TAggregate}"/>.
/// O filho exercitado e <see cref="FaturaEvento"/> (PK gerada no factory) — o mesmo
/// tipo que o BUG-01 rebaixava a Modified.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class FaturaAggregatePersistenceTests(PostgreSqlDatabaseFixture fixture)
    : AggregatePersistenceContract<Fatura>(fixture)
{
    protected override async Task<Guid> SeedAsync(EasyStockDbContext db)
    {
        var empresaId = Guid.NewGuid();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(FaturaTestSeed.Empresa(empresaId));
        var fatura = FaturaTestSeed.FaturaEmitida(empresaId);
        db.Faturas.Add(fatura);
        await db.SaveChangesAsync();
        return fatura.Id;
    }

    protected override Task<Fatura?> LoadTrackedAsync(EasyStockDbContext db, Guid id) =>
        db.Faturas.IgnoreQueryFilters()
            .Include(f => f.Pagamentos)
            .Include(f => f.Eventos)
            .FirstOrDefaultAsync(f => f.Id == id);

    protected override void AddChild(Fatura aggregate) =>
        aggregate.Eventos.Add(FaturaEvento.Criar(aggregate.Id, TipoEventoFatura.PdfGerado, origem: "test"));

    protected override Task<int> CountChildrenAsync(EasyStockDbContext db, Guid id) =>
        db.FaturaEventos.IgnoreQueryFilters().CountAsync(e => e.FaturaId == id);

    protected override void MutateRoot(Fatura aggregate) =>
        aggregate.Observacoes = "mut-" + Guid.NewGuid().ToString("N");
}
