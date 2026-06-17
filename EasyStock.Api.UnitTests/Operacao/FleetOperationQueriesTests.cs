using EasyStock.Application.Operacao;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Operacao;

/// <summary>
/// Testes da query da tela Operação (issue 623) com EasyStockDbContext InMemory. Combina
/// venda do ERP (Vendas) + conta (assinatura/plano/ticket): situação por cliente, totais,
/// escopo (ativos + suspensos) e ordenação por quem mais precisa de atenção. Faturas vão
/// no teste Postgres (owned/json).
/// </summary>
public sealed class FleetOperationQueriesTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly DateTime _now = DateTime.UtcNow;

    private static readonly Guid Alpha = Guid.NewGuid();   // saudável
    private static readonly Guid Bravo = Guid.NewGuid();   // crítico (SLA) + sem vendas
    private static readonly Guid Charlie = Guid.NewGuid(); // suspenso
    private static readonly Guid Delta = Guid.NewGuid();   // atenção (trial acabando)

    public FleetOperationQueriesTests()
    {
        _db = FleetTestSeed.SuperAdminDb($"op-{Guid.NewGuid()}");
        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        var pAlpha = FleetTestSeed.Plano(100m, "Starter");
        var pBravo = FleetTestSeed.Plano(200m, "Plus");
        var pCharlie = FleetTestSeed.Plano(150m, "Pro");
        var pDelta = FleetTestSeed.Plano(99m, "Starter");
        _db.Planos.AddRange(pAlpha, pBravo, pCharlie, pDelta);

        _db.Empresas.AddRange(
            FleetTestSeed.Empresa(Alpha, "Alpha"),
            FleetTestSeed.Empresa(Bravo, "Bravo"),
            FleetTestSeed.Empresa(Charlie, "Charlie"),
            FleetTestSeed.Empresa(Delta, "Delta"));

        _db.AssinaturasEmpresa.AddRange(
            FleetTestSeed.Assinatura(Alpha, pAlpha.Id, StatusAssinatura.Ativa),
            FleetTestSeed.Assinatura(Bravo, pBravo.Id, StatusAssinatura.Ativa),
            FleetTestSeed.Assinatura(Charlie, pCharlie.Id, StatusAssinatura.Suspensa),
            FleetTestSeed.Assinatura(Delta, pDelta.Id, StatusAssinatura.Ativa, trialFim: _now.AddDays(2)));

        // Alpha: vendeu R$500 hoje (2 vendas) -> saudável.
        _db.Vendas.AddRange(
            FleetTestSeed.Venda(Alpha, 300m, _now),
            FleetTestSeed.Venda(Alpha, 200m, _now));

        // Bravo: última venda 10 dias atrás + 1 chamado SLA violado -> crítico.
        _db.Vendas.Add(FleetTestSeed.Venda(Bravo, 90m, _now.AddDays(-10)));
        _db.AdminTickets.Add(FleetTestSeed.Ticket(Bravo, slaViolado: true));

        // Delta: vendeu R$80 hoje, mas trial acaba em 2 dias -> atenção.
        _db.Vendas.Add(FleetTestSeed.Venda(Delta, 80m, _now));

        _db.SaveChanges();
    }

    [Fact]
    public async Task Agrega_totais_de_ativos_e_suspensos()
    {
        var r = await new FleetOperationQueries(_db).ObterAsync(_now, maxLinhas: 100);

        r.TotalClientes.Should().Be(4);              // 3 ativos + 1 suspenso
        r.Totais.ClientesAtivos.Should().Be(3);
        r.Totais.Suspensos.Should().Be(1);
        r.Totais.MrrAtivo.Should().Be(399m);         // 100 + 200 + 99 (só ativos)
        r.Totais.VendasHojeTotal.Should().Be(580m);  // 500 (Alpha) + 80 (Delta)
        r.Totais.PrecisamAtencao.Should().Be(3);     // Bravo, Charlie, Delta
        r.Totais.TicketsSlaViolado.Should().Be(1);
    }

    [Fact]
    public async Task Ordena_por_quem_precisa_de_atencao_e_traz_motivos_claros()
    {
        var r = await new FleetOperationQueries(_db).ObterAsync(_now, maxLinhas: 100);

        // críticos primeiro (Bravo tem 2 motivos, Charlie 1), depois atenção (Delta), depois ok (Alpha).
        r.Clientes.Select(c => c.Nome).Should().ContainInOrder("Bravo", "Charlie", "Delta", "Alpha");

        var bravo = r.Clientes.Single(c => c.Nome == "Bravo");
        bravo.StatusBand.Should().Be(FleetHealthScoring.BandCrit);
        bravo.Motivos.Should().Contain(new[] { FleetHealthScoring.MotivoSlaViolado, FleetHealthScoring.MotivoSemVendas });
        bravo.VendasHoje.Should().Be(0m);

        var charlie = r.Clientes.Single(c => c.Nome == "Charlie");
        charlie.StatusBand.Should().Be(FleetHealthScoring.BandCrit);
        charlie.StatusAssinatura.Should().Be("Suspensa");
        charlie.Motivos.Should().Contain(FleetHealthScoring.MotivoSuspensa);

        var delta = r.Clientes.Single(c => c.Nome == "Delta");
        delta.StatusBand.Should().Be(FleetHealthScoring.BandWarn);
        delta.Motivos.Should().Contain(FleetHealthScoring.MotivoTrialVencendo);
        delta.VendasHoje.Should().Be(80m);

        var alpha = r.Clientes.Single(c => c.Nome == "Alpha");
        alpha.StatusBand.Should().Be(FleetHealthScoring.BandOk);
        alpha.Motivos.Should().BeEmpty();
        alpha.VendasHoje.Should().Be(500m);
        alpha.VendasHojeCount.Should().Be(2);
    }

    [Fact]
    public async Task Capa_as_linhas_mas_mantem_o_total()
    {
        var r = await new FleetOperationQueries(_db).ObterAsync(_now, maxLinhas: 2);

        r.Clientes.Should().HaveCount(2);
        r.Clientes.Select(c => c.Nome).Should().ContainInOrder("Bravo", "Charlie");
        r.TotalClientes.Should().Be(4);
    }
}
