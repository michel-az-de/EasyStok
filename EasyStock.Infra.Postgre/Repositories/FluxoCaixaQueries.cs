using EasyStock.Application.UseCases.Common;
using EasyStock.Application.Common;
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
        var fimMes = inicioMes.AddMonths(1);

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
                        p.DataPagamento < fimMes)
            .SumAsync(p => (decimal?)p.Valor, ct) ?? 0m;

        var recebidoMes = await db.PagamentosParcela.AsNoTracking()
            .Where(p => p.EmpresaId == empresaId &&
                        p.Lado == TipoLadoFinanceiro.Receber &&
                        p.Status == StatusPagamentoParcela.Confirmado &&
                        p.DataPagamento >= inicioMes &&
                        p.DataPagamento < fimMes)
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

        // #2095 (F13): as linhas sao dias civis de Brasilia. O console manda o inicio e o fim do dia em -03:00
        // (03:00Z e 02:59:59.999Z do dia seguinte); data sem fuso e horario de Brasilia.
        var buckets = GerarBuckets(DiaCivil(DataUtc.InicioDoPeriodoUtc(inicio)!.Value), DiaCivil(DataUtc.FimDoPeriodoUtc(fim)!.Value), periodicidade);
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
        if (categoriaId.HasValue)
            pagamentos = pagamentos.Where(pg =>
                pg.ParcelaPagar!.ContaPagar!.CategoriaFinanceiraId == categoriaId.Value ||
                pg.ParcelaReceber!.ContaReceber!.CategoriaFinanceiraId == categoriaId.Value);
        if (centroCustoId.HasValue)
            pagamentos = pagamentos.Where(pg =>
                pg.ParcelaPagar!.ContaPagar!.CentroCustoId == centroCustoId.Value ||
                pg.ParcelaReceber!.ContaReceber!.CentroCustoId == centroCustoId.Value);

        // Uma consulta por série evita quatro consultas por bucket. A projeção direta
        // em tuplas também evita copiar cada lista antes da distribuição pelos buckets.
        // Vencimento e data civil (00:00Z ou 03:00Z do dia: o dia UTC e o dia civil); pagamento e instante real.
        var civilIni = HorarioBrasil.CivilComoInstanteUtc(buckets[0].Inicio);
        var civilFim = HorarioBrasil.CivilComoInstanteUtc(buckets[^1].FimExclusivo);
        var realIni = HorarioBrasil.InicioRealDoDiaUtc(buckets[0].Inicio);
        var realFim = HorarioBrasil.InicioRealDoDiaUtc(buckets[^1].FimExclusivo);

        var prevPagarRows = await pagar
            .Where(p => p.DataVencimento >= civilIni && p.DataVencimento < civilFim)
            .Select(p => new ValueTuple<DateTime, decimal>(p.DataVencimento, p.Valor))
            .ToListAsync(ct);
        var prevReceberRows = await receber
            .Where(p => p.DataVencimento >= civilIni && p.DataVencimento < civilFim)
            .Select(p => new ValueTuple<DateTime, decimal>(p.DataVencimento, p.Valor))
            .ToListAsync(ct);
        var realPagarRows = await pagamentos
            .Where(pg => pg.Lado == TipoLadoFinanceiro.Pagar &&
                         pg.DataPagamento >= realIni && pg.DataPagamento < realFim)
            .Select(pg => new ValueTuple<DateTime, decimal>(pg.DataPagamento, pg.Valor))
            .ToListAsync(ct);
        var realReceberRows = await pagamentos
            .Where(pg => pg.Lado == TipoLadoFinanceiro.Receber &&
                         pg.DataPagamento >= realIni && pg.DataPagamento < realFim)
            .Select(pg => new ValueTuple<DateTime, decimal>(pg.DataPagamento, pg.Valor))
            .ToListAsync(ct);

        var resultado = new List<FluxoBucketDto>(buckets.Count);
        foreach (var (bIni, bFim, rotulo) in buckets)
        {
            var (cIni, cFim) = (HorarioBrasil.CivilComoInstanteUtc(bIni), HorarioBrasil.CivilComoInstanteUtc(bFim));
            var (rIni, rFim) = (HorarioBrasil.InicioRealDoDiaUtc(bIni), HorarioBrasil.InicioRealDoDiaUtc(bFim));
            resultado.Add(new FluxoBucketDto(
                cIni, cFim.AddTicks(-1), rotulo,
                SomaNoIntervalo(prevPagarRows, cIni, cFim),
                SomaNoIntervalo(prevReceberRows, cIni, cFim),
                SomaNoIntervalo(realPagarRows, rIni, rFim),
                SomaNoIntervalo(realReceberRows, rIni, rFim)));
        }

        return resultado;
    }

    /// <summary>Teto de linhas do fluxo: um ano no diário, sete anos no semanal, trinta no mensal.</summary>
    public const int MaximoLinhas = 366;

    private static DateOnly DiaCivil(DateTime utc) =>
        utc <= DateTime.MinValue.AddDays(1) ? DateOnly.MinValue
        : utc >= DateTime.MaxValue.AddDays(-1) ? DateOnly.MaxValue
        : HorarioBrasil.DataOperacional(utc);

    // Soma os valores no intervalo [ini, fim) da linha. As linhas sao contiguas e nao se sobrepoem.
    private static decimal SomaNoIntervalo(List<(DateTime Data, decimal Valor)> rows, DateTime ini, DateTime fim)
    {
        decimal soma = 0m;
        foreach (var (data, valor) in rows)
            if (data >= ini && data < fim) soma += valor;
        return soma;
    }

    /// <summary>
    /// Linhas [Inicio, FimExclusivo) em dias civis, a ultima cortada no dia final. Passar de <see cref="MaximoLinhas"/>
    /// e recusado com mensagem (#2095, F13): antes cortava em 24 linhas sem avisar.
    /// </summary>
    private static List<(DateOnly Inicio, DateOnly FimExclusivo, string Rotulo)> GerarBuckets(
        DateOnly inicio, DateOnly fim, PeriodicidadeFluxo p)
    {
        if (fim < inicio) throw new ArgumentException("Período inválido: o fim é antes do início.");
        var ret = new List<(DateOnly, DateOnly, string)>();
        var cursor = inicio;
        while (cursor <= fim)
        {
            if (ret.Count == MaximoLinhas)
                throw new ArgumentException(
                    $"Período longo demais para a periodicidade {p}: no máximo {MaximoLinhas} linhas. Encurte o período ou use uma periodicidade maior.");
            var proximo = p switch
            {
                PeriodicidadeFluxo.Diario => cursor.AddDays(1),
                PeriodicidadeFluxo.Semanal => cursor.AddDays(7),
                _ => cursor.AddMonths(1)
            };
            var rotulo = p == PeriodicidadeFluxo.Mensal ? cursor.ToString("MM/yyyy") : cursor.ToString("dd/MM");
            ret.Add((cursor, proximo > fim ? fim.AddDays(1) : proximo, rotulo));
            cursor = proximo;
        }
        return ret;
    }
}
