using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Regressao BUG-022 (filtro): o filtro por status na listagem deve derivar Vencida
/// por data, consistente com o badge (StatusEfetivo). Uma conta Aberta com parcela
/// vencida (antes do job rodar) tem que aparecer no filtro Vencida e NAO no Aberta.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class ContaPagarRepositoryFiltroTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task ListarAsync_filtro_Vencida_deriva_por_data()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow.Date;
        db.SetMobileTenantContext(empresaId);

        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Emp",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        var cat = CategoriaFinanceira.Criar(empresaId, "Desp", TipoCategoriaFinanceira.Despesa);
        db.CategoriasFinanceiras.Add(cat);

        // Status armazenado = Aberta, mas parcela vencida ha 3d (job ainda nao rodou).
        var vencidaPorData = ContaPagar.Criar(empresaId, null, cat.Id, "Vencida por data", hoje);
        vencidaPorData.AdicionarParcela(1, 100m, hoje.AddDays(-3));
        vencidaPorData.Emitir();
        db.ContasPagar.Add(vencidaPorData);

        // Aberta normal (a vencer em 10d).
        var abertaFuturo = ContaPagar.Criar(empresaId, null, cat.Id, "Aberta futuro", hoje);
        abertaFuturo.AdicionarParcela(1, 100m, hoje.AddDays(10));
        abertaFuturo.Emitir();
        db.ContasPagar.Add(abertaFuturo);

        await db.SaveChangesAsync();

        var repo = new Infra.Postgre.Repositories.ContaPagarRepository(db);

        var (vencidas, _) = await repo.ListarAsync(empresaId, status: StatusContaFinanceira.Vencida);
        vencidas.Should().ContainSingle(c => c.Id == vencidaPorData.Id);
        vencidas.Should().NotContain(c => c.Id == abertaFuturo.Id);

        var (abertas, _) = await repo.ListarAsync(empresaId, status: StatusContaFinanceira.Aberta);
        abertas.Should().ContainSingle(c => c.Id == abertaFuturo.Id);
        abertas.Should().NotContain(c => c.Id == vencidaPorData.Id);
    }
}
