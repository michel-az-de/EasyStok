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

        // Parcelas "vivas" = nao paga, nao cancelada, E de conta COMMITADA (nao Rascunho).
        // Rascunho tem parcelas mas nao e obrigacao real -> fora das projecoes (BUG-021).
        var parcelasPagarVivas = db.ParcelasPagar.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Paga &&
                        p.Status != StatusParcela.Cancelada &&
                        p.ContaPagar!.Status != StatusContaFinanceira.Rascunho);
        var parcelasReceberVivas = db.ParcelasReceber.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Paga &&
                        p.Status != StatusParcela.Cancelada &&
                        p.ContaReceber!.Status != StatusContaFinanceira.Rascunho);

        // A vencer nos proximos 30d
        var aVencerPagar = await parcelasPagarVivas
            .Where(p => p.DataVencimento >= hoje && p.DataVencimento <= mais30)
            .SumAsync(p => (decimal?)(p.Valor - p.ValorPago), ct) ?? 0m;
        var aVencerReceber = await parcelasReceberVivas
            .Where(p => p.DataVencimento >= hoje && p.DataVencimento <= mais30)
            .SumAsync(p => (decimal?)(p.Valor - p.ValorPago), ct) ?? 0m;

        // Vencido = derivado por data (independe do status armazenado da parcela/conta)
        var vencidoPagar = await parcelasPagarVivas
            .Where(p => p.DataVencimento < hoje)
            .SumAsync(p => (decimal?)(p.Valor - p.ValorPago), ct) ?? 0m;
        var vencidoReceber = await parcelasReceberVivas
            .Where(p => p.DataVencimento < hoje)
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

        var qtdParcelasVencidasHoje =
            await parcelasPagarVivas.CountAsync(p => p.DataVencimento < hoje, ct)
            + await parcelasReceberVivas.CountAsync(p => p.DataVencimento < hoje, ct);

        // BUG-08 (QA v1.10 #674): contas DISTINTAS com parcela viva a vencer na janela 30d do VALOR.
        var qtdCpAVencer30 = await parcelasPagarVivas
            .Where(p => p.DataVencimento >= hoje && p.DataVencimento <= mais30)
            .Select(p => p.ContaPagarId).Distinct().CountAsync(ct);
        var qtdCrAVencer30 = await parcelasReceberVivas
            .Where(p => p.DataVencimento >= hoje && p.DataVencimento <= mais30)
            .Select(p => p.ContaReceberId).Distinct().CountAsync(ct);

        return new DashboardFinanceiroDto(
            aVencerPagar, aVencerReceber,
            vencidoPagar, vencidoReceber,
            pagoMes, recebidoMes,
            qtdCpAbertas, qtdCrAbertas,
            qtdParcelasVencidasHoje,
            qtdCpAVencer30, qtdCrAVencer30);
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

        // Exclui Rascunho: previsao de fluxo so conta obrigacoes commitadas (BUG-021).
        var pagar = db.ParcelasPagar.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Cancelada &&
                        p.ContaPagar!.Status != StatusContaFinanceira.Rascunho);
        var receber = db.ParcelasReceber.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Status != StatusParcela.Cancelada &&
                        p.ContaReceber!.Status != StatusContaFinanceira.Rascunho);
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

        // Antes: 4 SumAsync POR bucket dentro do loop (ate 24 buckets = 96 SELECTs
        // sequenciais no PG). Agora: 4 queries no total (uma por serie), projetando
        // (data, valor) do range inteiro dos buckets; a bucketizacao roda em memoria
        // com os MESMOS limites de GerarBuckets (preserva a semantica exata de
        // diario/semanal/mensal e o cap de 24). Medido: 96 -> 4 comandos EF
        // (FluxoBucketsPerfTests). Parcela/Pagamento.Valor sao decimal simples (nao
        // value-object com converter), entao a projecao traduz direto pro SQL.
        var inicioGeral = buckets[0].Inicio;
        var fimGeral = buckets[^1].Fim;

        var prevPagarRows = (await pagar
            .Where(p => p.DataVencimento >= inicioGeral && p.DataVencimento <= fimGeral)
            .Select(p => new { D = p.DataVencimento, V = p.Valor })
            .ToListAsync(ct)).Select(x => (x.D, x.V)).ToList();
        var prevReceberRows = (await receber
            .Where(p => p.DataVencimento >= inicioGeral && p.DataVencimento <= fimGeral)
            .Select(p => new { D = p.DataVencimento, V = p.Valor })
            .ToListAsync(ct)).Select(x => (x.D, x.V)).ToList();
        var realPagarRows = (await pagamentos
            .Where(pg => pg.Lado == TipoLadoFinanceiro.Pagar &&
                         pg.DataPagamento >= inicioGeral && pg.DataPagamento <= fimGeral)
            .Select(pg => new { D = pg.DataPagamento, V = pg.Valor })
            .ToListAsync(ct)).Select(x => (x.D, x.V)).ToList();
        var realReceberRows = (await pagamentos
            .Where(pg => pg.Lado == TipoLadoFinanceiro.Receber &&
                         pg.DataPagamento >= inicioGeral && pg.DataPagamento <= fimGeral)
            .Select(pg => new { D = pg.DataPagamento, V = pg.Valor })
            .ToListAsync(ct)).Select(x => (x.D, x.V)).ToList();

        var resultado = new List<FluxoBucketDto>(buckets.Count);
        foreach (var (bIni, bFim, rotulo) in buckets)
        {
            resultado.Add(new FluxoBucketDto(
                bIni, bFim, rotulo,
                SomaNoIntervalo(prevPagarRows, bIni, bFim),
                SomaNoIntervalo(prevReceberRows, bIni, bFim),
                SomaNoIntervalo(realPagarRows, bIni, bFim),
                SomaNoIntervalo(realReceberRows, bIni, bFim)));
        }

        return resultado;
    }

    // Soma os valores cujas datas caem no intervalo [ini, fim] do bucket. Os intervalos
    // de GerarBuckets sao contiguos e nao se sobrepoem, entao cada linha cai em no maximo
    // um bucket — mesma atribuicao do filtro SQL por-bucket anterior.
    private static decimal SomaNoIntervalo(List<(DateTime Data, decimal Valor)> rows, DateTime ini, DateTime fim)
    {
        decimal soma = 0m;
        foreach (var (data, valor) in rows)
            if (data >= ini && data <= fim) soma += valor;
        return soma;
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
