using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Admin.Faturamento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Admin;

/// <summary>
/// Handler do relatório MRR/ARR/Churn — Admin SaaS.
/// Consulta cross-tenant (sem filtro de EmpresaId).
/// </summary>
public sealed class MrrArrChurnHandler(EasyStockDbContext db)
    : IReportHandler<MrrArrChurnParams, MrrArrChurnRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(MrrArrChurnParams parametros)
    {
        var fileNameBase = $"mrr-arr-churn_{parametros.De:yyyy-MM}_a_{parametros.Ate:yyyy-MM}";
        return new ReportSchema(
            title:        "MRR/ARR/Churn",
            fileNameBase: fileNameBase,
            columns:
            [
                new("Competencia",          "Competência",              0),
                new("AssinaturasAtivas",    "Assinaturas ativas",       1),
                new("AssinaturasNovas",     "Novas",                    2),
                new("AssinaturasCanceladas","Canceladas",               3),
                new("AssinaturasSuspensas", "Suspensas",                4),
                new("Mrr",                  "MRR (R$)",                 5, "0.00"),
                new("Arr",                  "ARR (R$)",                 6, "0.00"),
                new("ChurnRatePercent",     "Churn (%)",                7, "0.00"),
                new("ReceitaRealizada",     "Receita realizada (R$)",   8, "0.00"),
                new("TicketMedio",          "Ticket médio (R$)",        9, "0.00"),
            ]);
    }

    public Task ValidateAsync(MrrArrChurnParams parametros, CancellationToken ct)
    {
        if (parametros.De > parametros.Ate)
            throw new ArgumentException(
                "A data final deve ser igual ou posterior à inicial.",
                nameof(parametros.Ate));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<MrrArrChurnRow> StreamAsync(
        MrrArrChurnParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Gera lista de meses no intervalo
        var meses = new List<(int Ano, int Mes)>();
        var cur = new DateOnly(parametros.De.Year, parametros.De.Month, 1);
        var fim = new DateOnly(parametros.Ate.Year, parametros.Ate.Month, 1);
        while (cur <= fim)
        {
            meses.Add((cur.Year, cur.Month));
            cur = cur.AddMonths(1);
        }

        foreach (var (ano, mes) in meses)
        {
            ct.ThrowIfCancellationRequested();

            var inicioMes = new DateTime(ano, mes, 1, 0, 0, 0, DateTimeKind.Utc);
            var fimMes    = inicioMes.AddMonths(1).AddTicks(-1);

            // Assinaturas ativas no mês
            var ativas = await db.AssinaturasEmpresa
                .IgnoreQueryFilters()
                .Where(a => a.DataInicio <= fimMes
                         && (a.DataFim == null || a.DataFim >= inicioMes)
                         && a.Status != StatusAssinatura.Cancelada)
                .Include(a => a.Plano)
                .ToListAsync(ct);

            var assinaturasAtivas     = ativas.Count;
            var mrr                   = ativas.Sum(a => a.Plano?.PrecoMensal ?? 0m);
            var arr                   = mrr * 12m;
            var ticketMedio           = assinaturasAtivas > 0 ? mrr / assinaturasAtivas : 0m;

            var novas = await db.AssinaturasEmpresa
                .IgnoreQueryFilters()
                .CountAsync(a => a.DataInicio >= inicioMes && a.DataInicio <= fimMes, ct);

            var canceladas = await db.AssinaturasEmpresa
                .IgnoreQueryFilters()
                .CountAsync(a => a.Status == StatusAssinatura.Cancelada
                              && a.DataFim >= inicioMes && a.DataFim <= fimMes, ct);

            var suspensas = await db.AssinaturasEmpresa
                .IgnoreQueryFilters()
                .CountAsync(a => a.Status == StatusAssinatura.Suspensa
                              && a.DataInicio <= fimMes
                              && (a.DataFim == null || a.DataFim >= inicioMes), ct);

            // Receita realizada = pagamentos confirmados no mês
            var receitaRealizada = await db.FaturaPagamentos
                .IgnoreQueryFilters()
                .Where(p => p.PagoEm >= inicioMes && p.PagoEm <= fimMes)
                .SumAsync(p => (decimal?)p.Valor, ct) ?? 0m;

            // Churn = canceladas / (ativas no início do mês)
            var ativasInicioMes = await db.AssinaturasEmpresa
                .IgnoreQueryFilters()
                .CountAsync(a => a.DataInicio < inicioMes
                              && (a.DataFim == null || a.DataFim >= inicioMes)
                              && a.Status != StatusAssinatura.Cancelada, ct);

            var churnRate = ativasInicioMes > 0
                ? Math.Round((decimal)canceladas / ativasInicioMes * 100m, 2)
                : 0m;

            yield return new MrrArrChurnRow(
                Competencia:              $"{ano:D4}-{mes:D2}",
                AssinaturasAtivas:        assinaturasAtivas,
                AssinaturasCanceladas:    canceladas,
                AssinaturasSuspensas:     suspensas,
                AssinaturasNovas:         novas,
                Mrr:                      mrr,
                Arr:                      arr,
                ChurnRatePercent:         churnRate,
                ReceitaRealizada:         receitaRealizada,
                TicketMedio:              ticketMedio);
        }
    }

    public MrrArrChurnParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<MrrArrChurnParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar MrrArrChurnParams.");
}
