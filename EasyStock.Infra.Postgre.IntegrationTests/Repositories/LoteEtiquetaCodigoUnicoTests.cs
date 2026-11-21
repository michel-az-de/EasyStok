using EasyStock.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2506: o código da etiqueta é "{lote.Codigo}-{seq:D4}" e o código do lote é único só por empresa
/// (LOT-aammdd-001 é o primeiro lote do dia de qualquer empresa). O índice único tem de ser por lote,
/// não global, senão a segunda empresa não finaliza o lote.
/// </summary>
public class LoteEtiquetaCodigoUnicoTests(PostgreSqlDatabaseFixture fixture)
    : IClassFixture<PostgreSqlDatabaseFixture>
{
    private const string CodigoLote = "LOT-261113-001";

    [SkippableFact]
    public async Task Duas_empresas_gravam_a_mesma_etiqueta_do_primeiro_lote_do_dia()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        await GravarLoteComEtiquetasAsync(Guid.NewGuid(), $"{CodigoLote}-0001");

        var act = () => GravarLoteComEtiquetasAsync(Guid.NewGuid(), $"{CodigoLote}-0001");

        await act.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task O_mesmo_lote_nao_aceita_duas_etiquetas_com_o_mesmo_codigo()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await fixture.ResetDatabaseAsync();

        var act = () => GravarLoteComEtiquetasAsync(Guid.NewGuid(), $"{CodigoLote}-0001", $"{CodigoLote}-0001");

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task GravarLoteComEtiquetasAsync(Guid empresaId, params string[] codigos)
    {
        await using var db = fixture.CreateDbContext();
        db.SetMobileTenantContext(empresaId);
        db.Empresas.Add(new Empresa
        {
            Id = empresaId, Nome = "Emp", Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow, AlteradoEm = DateTime.UtcNow,
        });
        var lote = Lote.Criar(empresaId, CodigoLote, DateTime.UtcNow);
        var item = new LoteItem
        {
            Id = Guid.NewGuid(), LoteId = lote.Id, Nome = "Bolo", Quantidade = codigos.Length, CriadoEm = DateTime.UtcNow,
        };
        lote.Itens.Add(item);
        var seq = 0;
        foreach (var codigo in codigos)
        {
            lote.Etiquetas.Add(new LoteEtiqueta
            {
                Id = Guid.NewGuid(), LoteId = lote.Id, LoteItemId = item.Id, Sequencial = ++seq,
                Codigo = codigo, CriadoEm = DateTime.UtcNow,
            });
        }
        db.Lotes.Add(lote);
        await db.SaveChangesAsync();
    }
}
