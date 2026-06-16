using System.Linq.Expressions;
using EasyStock.Application.Common;
using EasyStock.Application.Operacao;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementacao Postgre do read model do Centro de Comando da Frota (issue 623).
/// Rollup operacional cross-tenant das lojas ATIVAS, computado com agregacoes
/// pre-filtradas (GROUP BY EmpresaId) unidas em memoria por empresa — sem
/// somas condicionais (evita falha de traducao do EF) e sem N+1 (nao chama o
/// cockpit por tenant). Os predicados de "entregue hoje", "aberto", "travado",
/// "conferencia" e "device ativo" vem de <see cref="OperacaoCriterios"/>, a mesma
/// fonte usada pelo cockpit, garantindo PARIDADE com /operacao por loja.
/// </summary>
public sealed class FleetOperationQueries(EasyStockDbContext db) : IFleetOperationQueries
{
    public async Task<FleetOperationSummary> ObterAsync(DateTime nowUtc, int maxLinhas, CancellationToken ct = default)
    {
        var (inicioDiaUtc, _) = HorarioBrasil.JanelaDiaUtc();

        // Escopo: tenants com assinatura ATIVA. Carrega cru e resolve nome/plano por
        // dicionario — sem navegar em projecao (portavel InMemory/Npgsql, evita INNER JOIN
        // que descartava linhas e nao depende de fixup de navegacao).
        var ativosRaw = await db.AssinaturasEmpresa.AsNoTracking()
            .Where(a => a.Status == StatusAssinatura.Ativa)
            .Select(a => new { a.EmpresaId, a.PlanoId, a.TrialFim })
            .ToListAsync(ct);

        var suspensos = await db.AssinaturasEmpresa.CountAsync(a => a.Status == StatusAssinatura.Suspensa, ct);

        if (ativosRaw.Count == 0)
        {
            return new FleetOperationSummary(nowUtc, 0,
                new FleetTotals(0, 0m, 0, 0, 0, 0, 0m, 0m, suspensos),
                Array.Empty<FleetTenantRow>());
        }

        var empresaIds = ativosRaw.Select(a => a.EmpresaId).Distinct().ToList();
        var planoIds = ativosRaw.Select(a => a.PlanoId).Distinct().ToList();

        var nomes = await db.Empresas.AsNoTracking()
            .Where(e => empresaIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Nome })
            .ToDictionaryAsync(e => e.Id, e => e.Nome, ct);

        var planos = await db.Planos.AsNoTracking()
            .Where(p => planoIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Nome, p.PrecoMensal })
            .ToDictionaryAsync(p => p.Id, ct);

        var ativos = ativosRaw.Select(a =>
        {
            planos.TryGetValue(a.PlanoId, out var pl);
            nomes.TryGetValue(a.EmpresaId, out var nome);
            return new AtivoLinha(a.EmpresaId, nome ?? "", pl?.Nome, pl?.PrecoMensal ?? 0m, a.TrialFim);
        }).ToList();

        // Vendas entregues hoje por empresa (valor + count).
        var vendasList = await db.Set<Order>().AsNoTracking()
            .Where(o => o.EmpresaId != null && empresaIds.Contains(o.EmpresaId.Value))
            .Where(OperacaoCriterios.EntregueHoje(inicioDiaUtc))
            .GroupBy(o => o.EmpresaId)
            .Select(g => new { g.Key, Valor = g.Sum(o => o.Total), Count = g.Count() })
            .ToListAsync(ct);
        var vendas = vendasList.ToDictionary(x => x.Key!.Value, x => (x.Valor, x.Count));

        // Contagens de pedidos por empresa, cada uma pre-filtrada (sem soma condicional).
        var abertos = await ContarOrdersAsync(OperacaoCriterios.Aberto());
        var travados = await ContarOrdersAsync(OperacaoCriterios.Travado(nowUtc));
        var conferencia = await ContarOrdersAsync(OperacaoCriterios.ConferenciaPendente());

        // Devices: total (contavel) e ativos por empresa.
        var devicesTotal = await ContarDevicesAsync(OperacaoCriterios.DeviceContavel());
        var devicesAtivos = await ContarDevicesAsync(OperacaoCriterios.DeviceAtivo(nowUtc));

        // Tickets abertos e com SLA violado por empresa.
        var ticketsAbertos = await db.AdminTickets.AsNoTracking()
            .Where(t => empresaIds.Contains(t.EmpresaId)
                        && t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Resolvido)
            .GroupBy(t => t.EmpresaId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var ticketsSla = await db.AdminTickets.AsNoTracking()
            .Where(t => empresaIds.Contains(t.EmpresaId)
                        && t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Resolvido
                        && (t.SlaRespostaViolado || t.SlaResolucaoViolado))
            .GroupBy(t => t.EmpresaId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        // Faturas vencidas por empresa (count + valor).
        var faturasList = await db.Faturas.AsNoTracking()
            .Where(f => empresaIds.Contains(f.EmpresaId) && f.Status == StatusFatura.Vencida)
            .GroupBy(f => f.EmpresaId)
            .Select(g => new { g.Key, Count = g.Count(), Valor = g.Sum(f => f.Total) })
            .ToListAsync(ct);
        var faturas = faturasList.ToDictionary(x => x.Key, x => (x.Count, x.Valor));

        var rows = new List<FleetTenantRow>(ativos.Count);
        foreach (var a in ativos)
        {
            vendas.TryGetValue(a.EmpresaId, out var venda);
            var devAtivos = devicesAtivos.GetValueOrDefault(a.EmpresaId);
            var devTotal = devicesTotal.GetValueOrDefault(a.EmpresaId);
            var pedTravados = travados.GetValueOrDefault(a.EmpresaId);
            var tkSla = ticketsSla.GetValueOrDefault(a.EmpresaId);
            var faturaVencida = faturas.TryGetValue(a.EmpresaId, out var fat) && fat.Count > 0;

            var health = FleetHealthScoring.Compute(new FleetHealthSignals(
                VendasCount: venda.Count,
                PedidosTravados: pedTravados,
                DevicesAtivos: devAtivos,
                DevicesTotal: devTotal,
                TicketsSlaViolado: tkSla,
                FaturaVencida: faturaVencida,
                TrialFim: a.TrialFim), nowUtc);

            rows.Add(new FleetTenantRow(
                EmpresaId: a.EmpresaId,
                Nome: a.Nome,
                Plano: a.Plano,
                HealthScore: health.Score,
                HealthBand: health.Band,
                VendasHoje: venda.Valor,
                VendasCount: venda.Count,
                PedidosAbertos: abertos.GetValueOrDefault(a.EmpresaId),
                PedidosTravados: pedTravados,
                ConferenciaPendente: conferencia.GetValueOrDefault(a.EmpresaId),
                DevicesAtivos: devAtivos,
                DevicesTotal: devTotal,
                TicketsAbertos: ticketsAbertos.GetValueOrDefault(a.EmpresaId),
                TicketsSlaViolado: tkSla,
                FaturaVencida: faturaVencida,
                TrialFim: a.TrialFim,
                RiscoFlags: health.Flags));
        }

        var totals = new FleetTotals(
            TenantsOnline: rows.Count(r => r.DevicesAtivos > 0),
            VendasHojeTotal: rows.Sum(r => r.VendasHoje),
            PedidosTravados: rows.Sum(r => r.PedidosTravados),
            TenantsEmRisco: rows.Count(r => r.HealthScore < FleetHealthScoring.LimiarRisco),
            TicketsSlaViolado: rows.Sum(r => r.TicketsSlaViolado),
            FaturasVencidasCount: rows.Count(r => r.FaturaVencida),
            FaturasVencidasValor: faturas.Values.Sum(x => x.Valor),
            MrrAtivo: ativos.Sum(a => a.PrecoMensal),
            Suspensos: suspensos);

        // Pior-primeiro (menor health), capada server-side; TotalTenants = escopo completo.
        var ordenadas = rows
            .OrderBy(r => r.HealthScore)
            .ThenByDescending(r => r.VendasHoje)
            .Take(maxLinhas)
            .ToList();

        return new FleetOperationSummary(nowUtc, ativos.Count, totals, ordenadas);

        // --- locais ---
        async Task<Dictionary<Guid, int>> ContarOrdersAsync(Expression<Func<Order, bool>> filtro)
        {
            var lista = await db.Set<Order>().AsNoTracking()
                .Where(o => o.EmpresaId != null && empresaIds.Contains(o.EmpresaId.Value))
                .Where(filtro)
                .GroupBy(o => o.EmpresaId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(ct);
            return lista.ToDictionary(x => x.Key!.Value, x => x.Count);
        }

        Task<Dictionary<Guid, int>> ContarDevicesAsync(Expression<Func<MobileDevice, bool>> filtro)
            => db.Set<MobileDevice>().AsNoTracking()
                .Where(d => empresaIds.Contains(d.EmpresaId))
                .Where(filtro)
                .GroupBy(d => d.EmpresaId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    private sealed record AtivoLinha(Guid EmpresaId, string Nome, string? Plano, decimal PrecoMensal, DateTime? TrialFim);
}
