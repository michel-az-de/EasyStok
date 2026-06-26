using EasyStock.Application.Common;
using EasyStock.Application.Operacao;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Read model da tela Operação (issue 623, reescrita) — combina a CONTA do cliente
/// (assinatura/MRR/tickets/faturas) com a VENDA REAL do ERP (<c>db.Vendas</c>), cross-tenant.
///
/// Vendas/itens são protegidos por RLS; como é uma leitura cross-tenant deliberada de
/// SuperAdmin, abrimos com <see cref="EasyStockDbContext.UseRowLevelSecurityBypass"/> (mesmo
/// mecanismo dos jobs/seeds). A agregação de "vendas de hoje" espelha o padrão canônico de
/// <c>GetVendasHojeQuery</c> (janela <see cref="HorarioBrasil.JanelaDiaUtc"/>, soma de
/// <c>ValorTotal.Valor</c>). A situação de cada cliente vem de <see cref="FleetHealthScoring"/>.
/// </summary>
public sealed class FleetOperationQueries(EasyStockDbContext db) : IFleetOperationQueries
{
    public async Task<FleetOperationSummary> ObterAsync(DateTime nowUtc, int maxLinhas, CancellationToken ct = default)
    {
        // Leitura cross-tenant deliberada (SuperAdmin) — abre o RLS p/ ler vendas de todos.
        using var _ = db.UseRowLevelSecurityBypass();

        var (inicioDiaUtc, fimDiaUtc) = HorarioBrasil.JanelaDiaUtc();

        // Escopo: clientes Ativos ou Suspensos (suspenso = precisa de atenção). Cancelados fora.
        var contas = await db.AssinaturasEmpresa.AsNoTracking()
            .Where(a => a.Status == StatusAssinatura.Ativa || a.Status == StatusAssinatura.Suspensa)
            .Select(a => new { a.EmpresaId, a.PlanoId, a.Status, a.TrialFim })
            .ToListAsync(ct);

        var suspensos = contas.Count(c => c.Status == StatusAssinatura.Suspensa);

        if (contas.Count == 0)
        {
            return new FleetOperationSummary(nowUtc, 0,
                new FleetTotals(0, 0, 0m, 0m, 0, 0m, suspensos),
                Array.Empty<FleetTenantRow>());
        }

        var empresaIds = contas.Select(c => c.EmpresaId).Distinct().ToList();
        var planoIds = contas.Select(c => c.PlanoId).Distinct().ToList();

        var nomes = await db.Empresas.AsNoTracking()
            .Where(e => empresaIds.Contains(e.Id))
            .Select(e => new { e.Id, e.Nome })
            .ToDictionaryAsync(e => e.Id, e => e.Nome, ct);

        var planos = await db.Planos.AsNoTracking()
            .Where(p => planoIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Nome, p.PrecoMensal })
            .ToDictionaryAsync(p => p.Id, ct);

        // Vendas REAIS do dia (ERP): valor + contagem por empresa. Somar o value-object owned
        // (ValorTotal.Valor) dentro de um GroupBy NÃO traduz no Npgsql (o otimizador colapsa a
        // pré-projeção de volta) — pegou no teste Postgres; InMemory mascara. Então materializa
        // a projeção plana (só seleciona coluna, traduz) e agrupa em memória. Vendas de um único
        // dia são poucas, então o custo é baixo.
        var vendasHojeRaw = await db.Vendas.AsNoTracking()
            .Where(v => empresaIds.Contains(v.EmpresaId) && v.DataVenda >= inicioDiaUtc && v.DataVenda < fimDiaUtc)
            .Select(v => new { v.EmpresaId, Valor = v.ValorTotal.Valor })
            .ToListAsync(ct);
        var vendasHoje = vendasHojeRaw
            .GroupBy(x => x.EmpresaId)
            .ToDictionary(g => g.Key, g => (Valor: g.Sum(x => x.Valor), Count: g.Count()));

        // Última venda (sinal de atividade) por empresa — Max sobre coluna pura (DataVenda) traduz.
        var ultimaVendaList = await db.Vendas.AsNoTracking()
            .Where(v => empresaIds.Contains(v.EmpresaId))
            .GroupBy(v => v.EmpresaId)
            .Select(g => new { g.Key, Ultima = g.Max(v => v.DataVenda) })
            .ToListAsync(ct);
        var ultimaVenda = ultimaVendaList.ToDictionary(x => x.Key, x => x.Ultima);

        // Tickets em aberto e com SLA violado. Predicado canônico compartilhado (issue 692)
        // — é a fonte da definição que Dashboard e Diagnóstico agora também usam.
        var ticketsAbertos = await db.AdminTickets.AsNoTracking()
            .Where(AdminTicketFilters.EmAberto)
            .Where(t => empresaIds.Contains(t.EmpresaId))
            .GroupBy(t => t.EmpresaId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var ticketsSla = await db.AdminTickets.AsNoTracking()
            .Where(AdminTicketFilters.EmAberto)
            .Where(t => empresaIds.Contains(t.EmpresaId)
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

        var rows = new List<FleetTenantRow>(contas.Count);
        foreach (var c in contas)
        {
            planos.TryGetValue(c.PlanoId, out var pl);
            nomes.TryGetValue(c.EmpresaId, out var nome);
            vendasHoje.TryGetValue(c.EmpresaId, out var vh);
            faturas.TryGetValue(c.EmpresaId, out var fat);
            var temVenda = ultimaVenda.TryGetValue(c.EmpresaId, out var uv);
            var tkAbertos = ticketsAbertos.GetValueOrDefault(c.EmpresaId);
            var tkSla = ticketsSla.GetValueOrDefault(c.EmpresaId);

            var aval = FleetHealthScoring.Avaliar(new FleetHealthSignals(
                Suspensa: c.Status == StatusAssinatura.Suspensa,
                VendasHojeCount: vh.Count,
                UltimaVendaEm: temVenda ? uv : null,
                TicketsAbertos: tkAbertos,
                TicketsSlaViolado: tkSla,
                FaturasVencidasCount: fat.Count,
                TrialFim: c.TrialFim), nowUtc);

            rows.Add(new FleetTenantRow(
                EmpresaId: c.EmpresaId,
                Nome: nome ?? "(sem nome)",
                Plano: pl?.Nome,
                Mrr: pl?.PrecoMensal ?? 0m,
                StatusAssinatura: c.Status.ToString(),
                StatusBand: aval.Band,
                Motivos: aval.Motivos,
                VendasHoje: vh.Valor,
                VendasHojeCount: vh.Count,
                TicketsAbertos: tkAbertos,
                TicketsSlaViolado: tkSla,
                FaturasVencidasCount: fat.Count,
                FaturasVencidasValor: fat.Valor,
                UltimaVendaEm: temVenda ? uv : null,
                TrialFim: c.TrialFim,
                Severidade: aval.Severidade));
        }

        var totals = new FleetTotals(
            ClientesAtivos: contas.Count(c => c.Status == StatusAssinatura.Ativa),
            PrecisamAtencao: rows.Count(r => r.StatusBand != FleetHealthScoring.BandOk),
            VendasHojeTotal: rows.Sum(r => r.VendasHoje),
            MrrAtivo: contas.Where(c => c.Status == StatusAssinatura.Ativa)
                            .Sum(c => planos.TryGetValue(c.PlanoId, out var p) ? p.PrecoMensal : 0m),
            TicketsSlaViolado: rows.Sum(r => r.TicketsSlaViolado),
            FaturasVencidasValor: faturas.Values.Sum(x => x.Valor),
            Suspensos: suspensos);

        var ordenadas = rows
            .OrderByDescending(r => r.Severidade)
            .ThenByDescending(r => r.FaturasVencidasValor)
            .ThenByDescending(r => r.VendasHoje)
            .Take(maxLinhas)
            .ToList();

        return new FleetOperationSummary(nowUtc, contas.Count, totals, ordenadas);
    }
}
