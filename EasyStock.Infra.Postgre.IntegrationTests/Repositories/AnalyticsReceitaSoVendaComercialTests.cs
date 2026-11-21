using EasyStock.Application.UseCases.Caixa;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Infra.Postgre.IntegrationTests.Repositories;

/// <summary>
/// #2844: saída de Doação, Perda ou Ajuste com preço grava uma Venda com valor. Não é receita: o caixa já a ignora
/// (CaixaRepository.GetTotalVendasNoIntervaloAsync) e o analytics somava junto. Uma venda de R$ 30 e uma doação de R$ 100.
/// </summary>
[Collection("PostgreSqlTestCollection")]
public sealed class AnalyticsReceitaSoVendaComercialTests(PostgreSqlDatabaseFixture fixture)
{
    private async Task<(AnalyticsRepository Repo, Guid EmpresaId)> PrepararAsync()
    {
        var dbContext = fixture.CreateDbContext();
        var repo = new AnalyticsRepository(dbContext, new CaixaSaldoCalculator(new CaixaRepository(dbContext)));
        var empresaId = Guid.NewGuid();
        var agora = DateTime.UtcNow;
        dbContext.SetMobileTenantContext(empresaId);
        dbContext.Empresas.Add(new Empresa { Id = empresaId, Nome = "Empresa Doacao", Documento = empresaId.ToString("N")[..14], CriadoEm = agora, AlteradoEm = agora });
        dbContext.Vendas.Add(new Venda
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Canal = CanalVenda.LojaPropria,
            Natureza = NaturezaMovimentacaoEstoque.Venda, DataVenda = agora,
            ValorTotal = Dinheiro.FromDecimal(30m), CriadoEm = agora
        });
        dbContext.Vendas.Add(new Venda
        {
            Id = Guid.NewGuid(), EmpresaId = empresaId, Canal = CanalVenda.LojaPropria,
            Natureza = NaturezaMovimentacaoEstoque.Doacao, DataVenda = agora,
            ValorTotal = Dinheiro.FromDecimal(100m), CriadoEm = agora
        });
        await dbContext.SaveChangesAsync();
        return (repo, empresaId);
    }

    [SkippableFact]
    public async Task GetReceitaPorPeriodoAsync_ignora_doacao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (repo, empresaId) = await PrepararAsync();

        var mes = (await repo.GetReceitaPorPeriodoAsync(empresaId, 12)).Single();

        mes.ReceitaBruta.Should().Be(30m);
        mes.TotalVendas.Should().Be(1);
    }

    [SkippableFact]
    public async Task GetVendasPorCanalAsync_ignora_doacao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (repo, empresaId) = await PrepararAsync();

        var canal = (await repo.GetVendasPorCanalAsync(empresaId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1))).Single();

        canal.ReceitaTotal.Should().Be(30m);
        canal.TotalVendas.Should().Be(1);
    }

    [SkippableFact]
    public async Task GetDashboardKpisAsync_ignora_doacao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (repo, empresaId) = await PrepararAsync();

        var kpis = await repo.GetDashboardKpisAsync(empresaId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        kpis.Receita.Should().Be(30m);
        kpis.TicketMedio.Should().Be(30m);
    }

    [SkippableFact]
    public async Task GetReceitaCustoSerieAsync_ignora_doacao()
    {
        Skip.If(!fixture.IsAvailable, fixture.UnavailableReason ?? "Docker/PostgreSQL unavailable");
        var (repo, empresaId) = await PrepararAsync();

        var serie = await repo.GetReceitaCustoSerieAsync(empresaId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        serie.Sum(d => d.Receita).Should().Be(30m);
    }
}
