using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Queries agregadas pra dashboard e fluxo de caixa.
/// EmpresaId obrigatorio em todos os metodos (anti-vazamento multi-tenant).
/// </summary>
public sealed class FluxoCaixaQueries(EasyStockDbContext db) : IFluxoCaixaQueries
{
    public async Task<DashboardFinanceiroDto> KpisDashboardAsync(Guid empresaId, DateTime referenceDateUtc, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty) throw new ArgumentException("EmpresaId obrigatorio.", nameof(empresaId));

        var hoje = referenceDateUtc.Date;
        var mais30 = hoje.AddDays(30);
        var inicioMes = new DateTime(hoje.Year, hoje.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var fimMes = inicioMes.AddMonths(1).AddSeconds(-1);

        // Parcelas a vencer 30d (pagar)
        var aVencerPagar = await db.ParcelasPagar.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Paga &&
                        p.Status != StatusParcela.Cancelada &&
                        p.DataVencimento >= hoje &&
                        p.DataVencimento <= mais30)
            .SumAsync(p => (decimal?)(p.Valor - p.ValorPago), ct) ?? 0m;

        var aVencerReceber = await db.ParcelasReceber.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Paga &&
                        p.Status != StatusParcela.Cancelada &&
                        p.DataVencimento >= hoje &&
                        p.DataVencimento <= mais30)
            .SumAsync(p => (decimal?)(p.Valor - p.ValorPago), ct) ?? 0m;

        var vencidoPagar = await db.ParcelasPagar.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Paga &&
                        p.Status != StatusParcela.Cancelada &&
                        p.DataVencimento < hoje)
            .SumAsync(p => (decimal?)(p.Valor - p.ValorPago), ct) ?? 0m;

        var vencidoReceber = await db.ParcelasReceber.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Paga &&
                        p.Status != StatusParcela.Cancelada &&
                        p.DataVencimento < hoje)
            .SumAsync(p => (decimal?)(p.Valor - p.ValorPago), ct) ?? 0m;

        var pagoMes = await db.PagamentosParcela.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Lado == TipoLadoFinanceiro.Pagar &&
                        p.Status == StatusPagamentoParcela.Confirmado &&
                        p.DataPagamento >= inicioMes &&
                        p.DataPagamento <= fimMes)
            .SumAsync(p => (decimal?)p.Valor, ct) ?? 0m;

        var recebidoMes = await db.PagamentosParcela.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Lado == TipoLadoFinanceiro.Receber &&
                        p.Status == StatusPagamentoParcela.Confirmado &&
                        p.DataPagamento >= inicioMes &&
                        p.DataPagamento <= fimMes)
            .SumAsync(p => (decimal?)p.Valor, ct) ?? 0m;

        var qtdCpAbertas = await db.ContasPagar.AsNoTracking()
            .CountAsync(c => c.EmpresaId == empresaId &&
                             (c.Status == StatusContaFinanceira.Aberta ||
                              c.Status == StatusContaFinanceira.ParcialmentePaga ||
                              c.Status == StatusContaFinanceira.Vencida), ct);

        var qtdCrAbertas = await db.ContasReceber.AsNoTracking()
            .CountAsync(c => c.EmpresaId == empresaId &&
                             (c.Status == StatusContaFinanceira.Aberta ||
                              c.Status == StatusContaFinanceira.ParcialmentePaga ||
                              c.Status == StatusContaFinanceira.Vencida), ct);

        var qtdParcelasVencidasHoje = await db.ParcelasPagar.AsNoTracking()
            .CountAsync(p => p.EmpresaId == empresaId &&
                             p.Status != StatusParcela.Paga &&
                             p.Status != StatusParcela.Cancelada &&
                             p.DataVencimento < hoje, ct)
            + await db.ParcelasReceber.AsNoTracking()
            .CountAsync(p => p.EmpresaId == empresaId &&
                             p.Status != StatusParcela.Paga &&
                             p.Status != StatusParcela.Cancelada &&
                             p.DataVencimento < hoje, ct);

        return new DashboardFinanceiroDto(
            aVencerPagar, aVencerReceber,
            vencidoPagar, vencidoReceber,
            pagoMes, recebidoMes,
            qtdCpAbertas, qtdCrAbertas,
            qtdParcelasVencidasHoje);
    }

    public async Task<IReadOnlyList<FluxoBucketDto>> FluxoBucketsAsync(
        Guid empresaId,
        PeriodicidadeFluxo periodicidade,
        DateTime inicio,
        DateTime fim,
        Guid? categoriaId = null,
        Guid? centroCustoId = null,
        CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty) throw new ArgumentException("EmpresaId obrigatorio.", nameof(empresaId));
        if (fim <= inicio) throw new ArgumentException("Periodo invalido (fim <= inicio).");

        var buckets = GerarBuckets(inicio, fim, periodicidade);
        if (buckets.Count > 24) buckets = buckets.Take(24).ToList();

        var pagar = db.ParcelasPagar.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Cancelada);
        var receber = db.ParcelasReceber.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Cancelada);
        if (categoriaId.HasValue)
        {
            pagar = pagar.Where(p => p.ContaPagar!.CategoriaFinanceiraId == categoriaId.Value);
            receber = receber.Where(p => p.ContaReceber!.CategoriaFinanceiraId == categoriaId.Value);
        }
        if (centroCustoId.HasValue)
        {
            pagar = pagar.Where(p => p.ContaPagar!.CentroCustoId == centroCustoId.Value);
            receber = receber.Where(p => p.ContaReceber!.CentroCustoId == centroCustoId.Value);
        }

        var pagamentos = db.PagamentosParcela.AsNoTracking()
            .Where(pg => pg.EmpresaId == empresaId &&
                         pg.Status == StatusPagamentoParcela.Confirmado);

        var resultado = new List<FluxoBucketDto>(buckets.Count);
        foreach (var (bIni, bFim, rotulo) in buckets)
        {
            var prevPagar = await pagar
                .Where(p => p.DataVencimento >= bIni && p.DataVencimento <= bFim)
                .SumAsync(p => (decimal?)p.Valor, ct) ?? 0m;
            var prevReceber = await receber
                .Where(p => p.DataVencimento >= bIni && p.DataVencimento <= bFim)
                .SumAsync(p => (decimal?)p.Valor, ct) ?? 0m;
            var realPagar = await pagamentos
                .Where(pg => pg.Lado == TipoLadoFinanceiro.Pagar &&
                             pg.DataPagamento >= bIni && pg.DataPagamento <= bFim)
                .SumAsync(pg => (decimal?)pg.Valor, ct) ?? 0m;
            var realReceber = await pagamentos
                .Where(pg => pg.Lado == TipoLadoFinanceiro.Receber &&
                             pg.DataPagamento >= bIni && pg.DataPagamento <= bFim)
                .SumAsync(pg => (decimal?)pg.Valor, ct) ?? 0m;

            resultado.Add(new FluxoBucketDto(bIni, bFim, rotulo, prevPagar, prevReceber, realPagar, realReceber));
        }

        return resultado;
    }

    private static List<(DateTime Inicio, DateTime Fim, string Rotulo)> GerarBuckets(
        DateTime inicio, DateTime fim, PeriodicidadeFluxo p)
    {
        var ret = new List<(DateTime, DateTime, string)>();
        var cursor = inicio.Date;
        while (cursor <= fim.Date)
        {
            DateTime bFim;
            string rotulo;
            switch (p)
            {
                case PeriodicidadeFluxo.Diario:
                    bFim = cursor.AddDays(1).AddSeconds(-1);
                    rotulo = cursor.ToString("dd/MM");
                    break;
                case PeriodicidadeFluxo.Semanal:
                    bFim = cursor.AddDays(7).AddSeconds(-1);
                    rotulo = $"{cursor:dd/MM}";
                    break;
                case PeriodicidadeFluxo.Mensal:
                default:
                    bFim = cursor.AddMonths(1).AddSeconds(-1);
                    rotulo = cursor.ToString("MM/yyyy");
                    break;
            }
            if (bFim > fim) bFim = fim;
            ret.Add((cursor, bFim, rotulo));

            cursor = p switch
            {
                PeriodicidadeFluxo.Diario => cursor.AddDays(1),
                PeriodicidadeFluxo.Semanal => cursor.AddDays(7),
                _ => cursor.AddMonths(1)
            };
        }
        return ret;
    }
}
