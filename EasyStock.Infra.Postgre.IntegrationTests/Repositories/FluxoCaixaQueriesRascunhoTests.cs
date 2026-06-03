using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// Regressao BUG-021: parcelas de conta RASCUNHO (rascunho nao emitido) NAO podem
/// entrar nas projecoes/KPIs do dashboard financeiro — rascunho nao e obrigacao real.
/// Integration (Postgres) porque o filtro depende de JOIN na conta-pai (p.ContaPagar.Status),
/// que o provider InMemory nao resolve.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class FluxoCaixaQueriesRascunhoTests(PostgreSqlDatabaseFixture fixture)
{
    [SkippableFact]
    public async Task KpisDashboard_exclui_parcelas_de_conta_Rascunho()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        await using var db = fixture.CreateDbContext();

        var empresaId = Guid.NewGuid();
        var hoje = DateTime.UtcNow.Date;
        db.SetMobileTenantContext(empresaId);

        db.Empresas.Add(new Empresa
        {
            Id = empresaId,
            Nome = "Empresa Teste",
            Documento = empresaId.ToString("N")[..14],
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        });
        var cat = CategoriaFinanceira.Criar(empresaId, "Despesas", TipoCategoriaFinanceira.Despesa);
        db.CategoriasFinanceiras.Add(cat);

        // Conta COMMITADA (Aberta): a vencer em 15d (100) + vencida ha 5d (50).
        var aberta = ContaPagar.Criar(empresaId, null, cat.Id, "Conta Aberta", hoje);
        aberta.AdicionarParcela(1, 100m, hoje.AddDays(15));
        aberta.AdicionarParcela(2, 50m, hoje.AddDays(-5));
        aberta.Emitir();
        db.ContasPagar.Add(aberta);

        // Conta RASCUNHO (nao emitida): mesma estrutura com valores grandes -> deve ficar FORA.
        var rascunho = ContaPagar.Criar(empresaId, null, cat.Id, "Conta Rascunho", hoje);
        rascunho.AdicionarParcela(1, 999m, hoje.AddDays(15));
        rascunho.AdicionarParcela(2, 777m, hoje.AddDays(-5));
        db.ContasPagar.Add(rascunho);

        await db.SaveChangesAsync();

        var kpis = await new Infra.Postgre.Repositories.FluxoCaixaQueries(db)
            .KpisDashboardAsync(empresaId, hoje);

        // So a conta Aberta entra; o Rascunho (999/777) fica fora.
        kpis.TotalAVencer30dPagar.Should().Be(100m);
        kpis.TotalVencidoPagar.Should().Be(50m);
        kpis.QtdParcelasVencidasHoje.Should().Be(1);
    }
}
