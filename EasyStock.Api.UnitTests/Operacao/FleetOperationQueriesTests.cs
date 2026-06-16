using EasyStock.Application.Common;
using EasyStock.Application.Operacao;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Operacao;

/// <summary>
/// Testes da agregacao da frota (issue 623) com EasyStockDbContext InMemory. Cobre
/// totais, linhas por tenant, escopo (so ativos), ordenacao pior-primeiro e o cap.
/// Faturas vencidas ficam no teste Postgres (Testcontainers) por causa do mapeamento
/// owned/json que o InMemory nao reproduz.
/// </summary>
public sealed class FleetOperationQueriesTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly DateTime _now = DateTime.UtcNow;

    // Empresas do cenario.
    private static readonly Guid Alpha = Guid.NewGuid();   // saudavel
    private static readonly Guid Bravo = Guid.NewGuid();   // em risco (offline + sem vendas + sla)
    private static readonly Guid Charlie = Guid.NewGuid(); // medio (1 travado)
    private static readonly Guid Delta = Guid.NewGuid();   // suspenso (fora do board)

    public FleetOperationQueriesTests()
    {
        _db = FleetTestSeed.SuperAdminDb($"fleet-{Guid.NewGuid()}");
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
            FleetTestSeed.Assinatura(Charlie, pCharlie.Id, StatusAssinatura.Ativa),
            FleetTestSeed.Assinatura(Delta, pDelta.Id, StatusAssinatura.Suspensa));

        // Alpha: 2 vendas hoje (R$100), 2 devices ativos. Saudavel.
        _db.Set<Domain.Entities.Mobile.Order>().AddRange(
            FleetTestSeed.Pedido(Alpha, OperacaoCriterios.StatusEntregue, 50m, _now),
            FleetTestSeed.Pedido(Alpha, OperacaoCriterios.StatusEntregue, 50m, _now));
        _db.Set<Domain.Entities.Mobile.MobileDevice>().AddRange(
            FleetTestSeed.Device(Alpha, _now),
            FleetTestSeed.Device(Alpha, _now));

        // Bravo: sem vendas, 1 device offline (visto ha 3h), 1 ticket SLA violado.
        _db.Set<Domain.Entities.Mobile.MobileDevice>().Add(
            FleetTestSeed.Device(Bravo, _now.AddHours(-3)));
        _db.AdminTickets.Add(FleetTestSeed.Ticket(Bravo, slaViolado: true));

        // Charlie: 1 venda hoje (R$80), 1 pedido travado (preparando ha 60min), 1 device ativo.
        _db.Set<Domain.Entities.Mobile.Order>().AddRange(
            FleetTestSeed.Pedido(Charlie, OperacaoCriterios.StatusEntregue, 80m, _now),
            FleetTestSeed.Pedido(Charlie, OperacaoCriterios.StatusPreparando, 40m, _now.AddMinutes(-60), _now.AddMinutes(-60)));
        _db.Set<Domain.Entities.Mobile.MobileDevice>().Add(FleetTestSeed.Device(Charlie, _now));

        _db.SaveChanges();
    }

    [Fact]
    public async Task Agrega_totais_da_frota_apenas_dos_tenants_ativos()
    {
        var sut = new FleetOperationQueries(_db);

        var r = await sut.ObterAsync(_now, maxLinhas: 100);

        r.TotalTenants.Should().Be(3); // Delta (suspenso) fora
        r.Totals.Suspensos.Should().Be(1);
        r.Totals.MrrAtivo.Should().Be(450m); // 100 + 200 + 150
        r.Totals.VendasHojeTotal.Should().Be(180m); // 100 + 0 + 80
        r.Totals.TenantsOnline.Should().Be(2); // Alpha, Charlie
        r.Totals.TenantsEmRisco.Should().Be(1); // Bravo (< 70)
        r.Totals.PedidosTravados.Should().Be(1); // Charlie
        r.Totals.TicketsSlaViolado.Should().Be(1); // Bravo
        r.Totals.FaturasVencidasCount.Should().Be(0); // faturas cobertas no teste Postgres
    }

    [Fact]
    public async Task Ordena_pior_primeiro_e_marca_flags_de_risco()
    {
        var sut = new FleetOperationQueries(_db);

        var r = await sut.ObterAsync(_now, maxLinhas: 100);

        r.Tenants.Select(t => t.Nome).Should().ContainInOrder("Bravo", "Charlie", "Alpha");

        var bravo = r.Tenants.Single(t => t.Nome == "Bravo");
        bravo.HealthBand.Should().Be(FleetHealthScoring.BandCrit);
        bravo.DevicesAtivos.Should().Be(0);
        bravo.DevicesTotal.Should().Be(1);
        bravo.VendasCount.Should().Be(0);
        bravo.RiscoFlags.Should().Contain(new[]
        {
            FleetHealthScoring.FlagDevicesOffline,
            FleetHealthScoring.FlagSemVendas,
            FleetHealthScoring.FlagSlaViolado,
        });

        var charlie = r.Tenants.Single(t => t.Nome == "Charlie");
        charlie.VendasHoje.Should().Be(80m);
        charlie.PedidosTravados.Should().Be(1);
        charlie.RiscoFlags.Should().Contain(FleetHealthScoring.FlagPedidosTravados);

        var alpha = r.Tenants.Single(t => t.Nome == "Alpha");
        alpha.HealthScore.Should().Be(100);
        alpha.VendasHoje.Should().Be(100m);
        alpha.VendasCount.Should().Be(2);
        alpha.DevicesAtivos.Should().Be(2);
        alpha.RiscoFlags.Should().BeEmpty();
    }

    [Fact]
    public async Task Capa_as_linhas_mas_mantem_o_total_para_mostrar_X_de_N()
    {
        var sut = new FleetOperationQueries(_db);

        var r = await sut.ObterAsync(_now, maxLinhas: 2);

        r.Tenants.Should().HaveCount(2);
        r.Tenants.Select(t => t.Nome).Should().ContainInOrder("Bravo", "Charlie");
        r.TotalTenants.Should().Be(3);
    }
}
