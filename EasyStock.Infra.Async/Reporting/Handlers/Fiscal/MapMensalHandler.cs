using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Fiscal.MapMensal;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Fiscal;

/// <summary>
/// Handler do relatório "MAP — Mapa Resumo NFC-e" — Fase 2.
/// Agrega NFC-e autorizadas e canceladas por dia, incluindo totais de ICMS/PIS/COFINS.
/// Máximo estimado de 31 linhas (um por dia).
/// </summary>
public sealed class MapMensalHandler(
    EasyStockDbContext db,
    ITenantScopedQueryBuilder tenantQuery)
    : IReportHandler<MapMensalParams, MapMensalRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(MapMensalParams parametros)
    {
        var competencia = $"{parametros.De:yyyy-MM}";
        return new ReportSchema(
            title: "MAP — Mapa Resumo NFC-e",
            fileNameBase: $"map-nfce_{competencia}",
            columns:
            [
                new("Data",              "Data",                    0, "dd/MM/yyyy"),
                new("QtdAutorizadas",    "Qtd. Autorizadas",        1),
                new("QtdCanceladas",     "Qtd. Canceladas",         2),
                new("TotalAutorizadas",  "Total Autorizadas (R$)",  3, "0.00"),
                new("TotalCanceladas",   "Total Canceladas (R$)",   4, "0.00"),
                new("TotalLiquido",      "Total Líquido (R$)",      5, "0.00"),
                new("TotalIcms",         "ICMS (R$)",               6, "0.00"),
                new("TotalPis",          "PIS (R$)",                7, "0.00"),
                new("TotalCofins",       "COFINS (R$)",             8, "0.00"),
            ]);
    }

    public async Task ValidateAsync(MapMensalParams parametros, CancellationToken ct)
    {
        if (parametros.De > parametros.Ate)
            throw new ArgumentException(
                "A data final deve ser igual ou posterior à inicial.",
                nameof(parametros.Ate));

        // MAP é por competência mensal — limitar a 31 dias.
        if (parametros.Ate.DayNumber - parametros.De.DayNumber > 31)
            throw new ArgumentException(
                "O MAP Resumo é por competência mensal. Selecione no máximo 31 dias.",
                nameof(parametros.Ate));
    }

    public async IAsyncEnumerable<MapMensalRow> StreamAsync(
        MapMensalParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var de = parametros.De.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var ate = parametros.Ate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);

        // Passo 1: Carregar todos os documentos do período (max ~31 dias × 500 NFC-e/dia = ~15k rows).
        var docs = await tenantQuery.Query<NfeDocumento>()
            .Where(n => (n.Status == StatusNfe.Autorizada || n.Status == StatusNfe.Cancelada)
                        && n.DataAutorizacao >= de && n.DataAutorizacao <= ate)
            .AsNoTracking()
            .ToListAsync(ct);

        if (docs.Count == 0)
            yield break;

        var docIds = docs.ConvertAll(d => d.Id);

        // Passo 2: Carregar tributos agregados por documento.
        var itens = await db.NfeItens
            .Where(i => docIds.Contains(i.NfeDocumentoId))
            .AsNoTracking()
            .ToListAsync(ct);

        // Dicionário: NfeDocumentoId → (Icms, Pis, Cofins)
        var tributosDoc = itens
            .GroupBy(i => i.NfeDocumentoId)
            .ToDictionary(
                g => g.Key,
                g => (
                    Icms: g.Sum(i => i.ValorIcms ?? 0m),
                    Pis: g.Sum(i => i.Pis ?? 0m),
                    Cofins: g.Sum(i => i.Cofins ?? 0m)));

        // Passo 3: Agrupar por dia e gerar linhas.
        var porDia = docs
            .GroupBy(d => DateOnly.FromDateTime(d.DataAutorizacao!.Value))
            .OrderBy(g => g.Key);

        foreach (var dia in porDia)
        {
            var autorizadas = dia.Where(d => d.Status == StatusNfe.Autorizada).ToList();
            var canceladas = dia.Where(d => d.Status == StatusNfe.Cancelada).ToList();

            var totalAut = autorizadas.Sum(d => d.TotalNota.Valor);
            var totalCan = canceladas.Sum(d => d.TotalNota.Valor);
            var totalIcms = dia.Sum(d => tributosDoc.TryGetValue(d.Id, out var t) ? t.Icms : 0m);
            var totalPis = dia.Sum(d => tributosDoc.TryGetValue(d.Id, out var t) ? t.Pis : 0m);
            var totalCof = dia.Sum(d => tributosDoc.TryGetValue(d.Id, out var t) ? t.Cofins : 0m);

            yield return new MapMensalRow(
                Data: dia.Key,
                QtdAutorizadas: autorizadas.Count,
                QtdCanceladas: canceladas.Count,
                TotalAutorizadas: totalAut,
                TotalCanceladas: totalCan,
                TotalLiquido: totalAut - totalCan,
                TotalIcms: totalIcms,
                TotalPis: totalPis,
                TotalCofins: totalCof);
        }
    }

    public MapMensalParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<MapMensalParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar MapMensalParams.");
}
