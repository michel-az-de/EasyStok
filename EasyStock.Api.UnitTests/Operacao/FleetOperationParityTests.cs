using EasyStock.Api.Mobile.Controllers;
using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Operacao;

/// <summary>
/// PARIDADE (issue 623): para um mesmo tenant, a linha da frota deve casar campo a
/// campo com o cockpit por loja (OperationController.GetDashboard). Ambos derivam dos
/// mesmos criterios em <see cref="OperacaoCriterios"/>; este teste prova que nao ha
/// divergencia nos campos compartilhados.
/// </summary>
public sealed class FleetOperationParityTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly DateTime _now = DateTime.UtcNow;
    private static readonly Guid EmpresaId = Guid.NewGuid();

    public FleetOperationParityTests()
    {
        _db = FleetTestSeed.SuperAdminDb($"fleet-parity-{Guid.NewGuid()}");
        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        var plano = FleetTestSeed.Plano(120m);
        _db.Planos.Add(plano);
        _db.Empresas.Add(FleetTestSeed.Empresa(EmpresaId, "Paridade"));
        _db.AssinaturasEmpresa.Add(FleetTestSeed.Assinatura(EmpresaId, plano.Id, StatusAssinatura.Ativa));

        _db.Set<Domain.Entities.Mobile.Order>().AddRange(
            FleetTestSeed.Pedido(EmpresaId, OperacaoCriterios.StatusEntregue, 30m, _now),
            FleetTestSeed.Pedido(EmpresaId, OperacaoCriterios.StatusEntregue, 70m, _now),
            FleetTestSeed.Pedido(EmpresaId, OperacaoCriterios.StatusPreparando, 10m, _now.AddMinutes(-50), _now.AddMinutes(-50)), // travado
            FleetTestSeed.Pedido(EmpresaId, OperacaoCriterios.StatusPreparando, 10m, _now, _now),                                  // aberto, nao travado
            FleetTestSeed.Pedido(EmpresaId, OperacaoCriterios.StatusPronto, 10m, _now, _now, confirmedAt: null),                   // conferencia pendente
            FleetTestSeed.Pedido(EmpresaId, OperacaoCriterios.StatusPronto, 10m, _now, _now, confirmedAt: _now),                   // pronto, ja conferido
            FleetTestSeed.Pedido(EmpresaId, OperacaoCriterios.StatusCancelado, 10m, _now));                                        // ignorado

        _db.Set<Domain.Entities.Mobile.MobileDevice>().AddRange(
            FleetTestSeed.Device(EmpresaId, _now),
            FleetTestSeed.Device(EmpresaId, _now.AddMinutes(-5)),
            FleetTestSeed.Device(EmpresaId, _now.AddHours(-5)),         // offline
            FleetTestSeed.Device(EmpresaId, _now, revoked: true));      // revogado (fora do total)

        _db.SaveChanges();
    }

    [Fact]
    public async Task Linha_da_frota_bate_com_o_cockpit_campo_a_campo()
    {
        // Cockpit (por loja).
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.Nivel.Returns(NivelAcesso.SuperAdmin);
        var controller = new OperationController(_db, null!, currentUser, null!, NullLogger<OperationController>.Instance);
        var action = await controller.GetDashboard(EmpresaId, null, CancellationToken.None);
        var dash = (action.Result as OkObjectResult)!.Value as OperationDashboard;
        dash.Should().NotBeNull();

        // Frota (cross-tenant) — mesma fonte de criterios.
        var fleet = await new FleetOperationQueries(_db).ObterAsync(_now, maxLinhas: 100);
        var row = fleet.Tenants.Single(t => t.EmpresaId == EmpresaId);

        row.VendasHoje.Should().Be(dash!.VendasHojeValor);
        row.VendasCount.Should().Be(dash.VendasHojeCount);
        row.PedidosAbertos.Should().Be(dash.PedidosAbertos);
        row.PedidosTravados.Should().Be(dash.PedidosTravados);
        row.ConferenciaPendente.Should().Be(dash.ConferenciaPendente);
        row.DevicesAtivos.Should().Be(dash.DevicesAtivos);
        row.DevicesTotal.Should().Be(dash.DevicesTotal);

        // sanidade do cenario
        dash.VendasHojeCount.Should().Be(2);
        dash.PedidosAbertos.Should().Be(4);
        dash.PedidosTravados.Should().Be(1);
        dash.ConferenciaPendente.Should().Be(1);
        dash.DevicesAtivos.Should().Be(2);
        dash.DevicesTotal.Should().Be(3);
    }
}
