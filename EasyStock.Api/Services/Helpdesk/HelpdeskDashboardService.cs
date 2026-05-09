using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Services.Helpdesk;

public sealed record DashboardItemContagem(string Chave, int Quantidade);

public sealed record HelpdeskDashboardResultado(
    int Abertos,
    int EmAtendimento,
    int AguardandoCliente,
    int Resolvidos,
    int Fechados,
    int VencidosSla,
    int ResolvidosHoje,
    double? TempoMedioRespostaHoras,
    double? TempoMedioResolucaoHoras,
    double? SatisfacaoMedia,
    int TotalAvaliacoes,
    IReadOnlyList<DashboardItemContagem> TicketsPorCategoria,
    IReadOnlyList<DashboardItemContagem> TicketsPorPrioridade);

/// <summary>
/// Agrega metricas de helpdesk filtradas por empresa (multi-tenant).
/// Consumido por GET /api/helpdesk/dashboard. Usa queries Postgres
/// agregadas: 4 idas ao banco (status, tempos+csat, categoria, prioridade).
/// </summary>
public sealed class HelpdeskDashboardService(EasyStockDbContext db)
{
    public async Task<HelpdeskDashboardResultado> ObterAsync(Guid empresaId, CancellationToken ct = default)
    {
        var hojeUtc = DateTime.UtcNow.Date;
        var amanhaUtc = hojeUtc.AddDays(1);

        var statusCounts = await db.AdminTickets
            .AsNoTracking()
            .Where(t => t.EmpresaId == empresaId)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Quantidade = g.Count() })
            .ToListAsync(ct);

        var resolvidosHoje = await db.AdminTickets
            .AsNoTracking()
            .Where(t => t.EmpresaId == empresaId
                && t.Status == TicketStatus.Resolvido
                && t.ResolvidoEm != null
                && t.ResolvidoEm >= hojeUtc
                && t.ResolvidoEm < amanhaUtc)
            .CountAsync(ct);

        var vencidosSla = await db.AdminTickets
            .AsNoTracking()
            .Where(t => t.EmpresaId == empresaId
                && t.Status != TicketStatus.Resolvido
                && t.Status != TicketStatus.Fechado
                && (t.SlaRespostaViolado || t.SlaResolucaoViolado))
            .CountAsync(ct);

        // Tempos medios + CSAT — projetamos timestamps e nota; agregamos em memoria
        // pra evitar dependencia em func dialeto-especifica (DATEDIFF). Volume real
        // de tickets por empresa cabe em paginacao default; se crescer, mover pra
        // SQL puro com EXTRACT(EPOCH FROM (resolvido_em - criado_em)) / 60.
        var amostra = await db.AdminTickets
            .AsNoTracking()
            .Where(t => t.EmpresaId == empresaId)
            .Select(t => new
            {
                t.CriadoEm,
                t.PrimeiraRespostaEm,
                t.ResolvidoEm,
                t.NotaCsat
            })
            .ToListAsync(ct);

        double? avgRespHoras = null, avgResolHoras = null, csatAvg = null;
        var totalAvaliacoes = 0;
        if (amostra.Count > 0)
        {
            var resp = amostra
                .Where(t => t.PrimeiraRespostaEm.HasValue)
                .Select(t => (t.PrimeiraRespostaEm!.Value - t.CriadoEm).TotalHours)
                .ToList();
            if (resp.Count > 0) avgRespHoras = Math.Round(resp.Average(), 2);

            var resol = amostra
                .Where(t => t.ResolvidoEm.HasValue)
                .Select(t => (t.ResolvidoEm!.Value - t.CriadoEm).TotalHours)
                .ToList();
            if (resol.Count > 0) avgResolHoras = Math.Round(resol.Average(), 2);

            var csats = amostra
                .Where(t => t.NotaCsat.HasValue)
                .Select(t => (double)t.NotaCsat!.Value)
                .ToList();
            totalAvaliacoes = csats.Count;
            if (csats.Count > 0) csatAvg = Math.Round(csats.Average(), 2);
        }

        var porCategoria = await db.AdminTickets
            .AsNoTracking()
            .Where(t => t.EmpresaId == empresaId)
            .GroupBy(t => t.Categoria)
            .Select(g => new DashboardItemContagem(g.Key.ToString(), g.Count()))
            .ToListAsync(ct);

        var porPrioridade = await db.AdminTickets
            .AsNoTracking()
            .Where(t => t.EmpresaId == empresaId)
            .GroupBy(t => t.Prioridade)
            .Select(g => new DashboardItemContagem(g.Key.ToString(), g.Count()))
            .ToListAsync(ct);

        int Get(TicketStatus s) => statusCounts.FirstOrDefault(x => x.Status == s)?.Quantidade ?? 0;

        return new HelpdeskDashboardResultado(
            Abertos: Get(TicketStatus.Aberto),
            EmAtendimento: Get(TicketStatus.EmAtendimento),
            AguardandoCliente: Get(TicketStatus.AguardandoCliente),
            Resolvidos: Get(TicketStatus.Resolvido),
            Fechados: Get(TicketStatus.Fechado),
            VencidosSla: vencidosSla,
            ResolvidosHoje: resolvidosHoje,
            TempoMedioRespostaHoras: avgRespHoras,
            TempoMedioResolucaoHoras: avgResolHoras,
            SatisfacaoMedia: csatAvg,
            TotalAvaliacoes: totalAvaliacoes,
            TicketsPorCategoria: porCategoria,
            TicketsPorPrioridade: porPrioridade);
    }
}
