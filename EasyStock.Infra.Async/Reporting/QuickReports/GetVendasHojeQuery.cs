using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.QuickReports;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.QuickReports;

/// <summary>
/// Quick Report: vendas-hoje — resumo do dia atual para o painel mobile.
/// Síncrono, &lt; 1s, sem paginação, sem persistir ReportRun (§27.7).
/// </summary>
public sealed class GetVendasHojeQuery(
    EasyStockDbContext    db,
    ICurrentUserAccessor  currentUser)
{
    public async Task<VendasHojeDto> ExecuteAsync(Guid? lojaId, CancellationToken ct)
    {
        var empresaId = currentUser.EmpresaId;
        // JanelaDiaUtc: [ini,fim) = meia-noite BRT em UTC (03:00Z–03:00Z+24h).
        // Antes UtcNow.Date (00:00Z) fazia o bucket "hoje" resetar as 21h BRT.
        var (hoje, amanha) = HorarioBrasil.JanelaDiaUtc();

        var vendasQuery = db.Vendas
            .AsNoTracking()
            .Where(v => v.EmpresaId == empresaId
                     && v.DataVenda >= hoje
                     && v.DataVenda < amanha);

        if (lojaId.HasValue)
            vendasQuery = vendasQuery.Where(v => v.LojaId == lojaId.Value);

        // Totalizadores básicos
        var totais = await vendasQuery
            .GroupBy(_ => true)
            .Select(g => new
            {
                QtdVendas  = g.Count(),
                TotalValor = g.Sum(v => (decimal?)v.ValorTotal.Valor) ?? 0m,
            })
            .FirstOrDefaultAsync(ct);

        var qtdVendas   = totais?.QtdVendas  ?? 0;
        var totalValor  = totais?.TotalValor ?? 0m;
        var ticketMedio = qtdVendas > 0 ? Math.Round(totalValor / qtdVendas, 2) : 0m;

        // Top-5 produtos do dia (via ItensVenda)
        var topProdutos = await db.ItensVenda
            .AsNoTracking()
            .Where(i => i.Venda!.EmpresaId == empresaId
                     && i.Venda.DataVenda >= hoje
                     && i.Venda.DataVenda < amanha   // mesmo intervalo BRT
                     && (lojaId == null || i.Venda.LojaId == lojaId))
            .GroupBy(i => new { i.ProdutoId, Descricao = i.DescricaoSnapshot })
            .Select(g => new TopProdutoDto(
                g.Key.ProdutoId,
                g.Key.Descricao ?? "—",
                g.Sum(i => i.Quantidade.Value)))
            .OrderByDescending(p => p.Qtd)
            .Take(5)
            .ToListAsync(ct);

        return new VendasHojeDto(totalValor, qtdVendas, ticketMedio, topProdutos);
    }
}
