using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Fiscal.TotalizadoresFiscais;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Fiscal;

/// <summary>
/// Handler do relatório "Totalizadores fiscais por CFOP/CST/NCM" — Fase 2.
/// Agrega itens de NFC-e autorizadas no período, agrupando por CFOP × CST/CSOSN × NCM.
/// Itens de NFC-e legadas (pré-PR-D) aparecem com tributos = R$ 0,00 e TributosRastreados=false.
/// </summary>
public sealed class TotalizadoresFiscaisHandler(
    EasyStockDbContext        db,
    ITenantScopedQueryBuilder tenantQuery)
    : IReportHandler<TotalizadoresFiscaisParams, TotalizadoresFiscaisRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(TotalizadoresFiscaisParams parametros)
    {
        var competencia = $"{parametros.De:yyyy-MM}";
        return new ReportSchema(
            title:        "Totalizadores fiscais por CFOP/CST/NCM",
            fileNameBase: $"nfce-totalizadores_{competencia}",
            columns:
            [
                new("Cfop",                "CFOP",                     0),
                new("CstOuCsosn",          "CST/CSOSN",                1),
                new("Ncm",                 "NCM",                      2),
                new("QtdItens",            "Qtd. Itens",               3),
                new("TotalItens",          "Total Itens (R$)",         4, "0.00"),
                new("BaseIcms",            "Base ICMS (R$)",           5, "0.00"),
                new("ValorIcms",           "ICMS (R$)",                6, "0.00"),
                new("Pis",                 "PIS (R$)",                 7, "0.00"),
                new("Cofins",              "COFINS (R$)",              8, "0.00"),
                new("TributosRastreados",  "Tributos rastreados?",     9),
            ]);
    }

    public async Task ValidateAsync(TotalizadoresFiscaisParams parametros, CancellationToken ct)
    {
        if (parametros.De > parametros.Ate)
            throw new ArgumentException(
                "A data final deve ser igual ou posterior à inicial.",
                nameof(parametros.Ate));

        if (parametros.Ate.DayNumber - parametros.De.DayNumber > 366)
            throw new ArgumentException(
                "Para períodos maiores que 12 meses, divida em gerações mensais.",
                nameof(parametros.Ate));
    }

    public async IAsyncEnumerable<TotalizadoresFiscaisRow> StreamAsync(
        TotalizadoresFiscaisParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var de  = parametros.De.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var ate = parametros.Ate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);

        // Passo 1: IDs das NFC-e autorizadas no período (escopo de tenant garantido).
        var docIds = await tenantQuery.Query<NfeDocumento>()
            .AsNoTracking()
            .Where(n => n.Status == StatusNfe.Autorizada
                        && n.DataAutorizacao >= de && n.DataAutorizacao <= ate)
            .Select(n => n.Id)
            .ToListAsync(ct);

        if (docIds.Count == 0)
            yield break;

        // Passo 2: Itens dos documentos selecionados.
        // NfeItem não tem EmpresaId — o escopo de tenant vem pelo JOIN implícito via docIds.
        var itens = await db.NfeItens
            .Where(i => docIds.Contains(i.NfeDocumentoId))
            .AsNoTracking()
            .ToListAsync(ct);

        // Passo 3: Agrupamento em memória (conjunto finito — poucos milhares de combinações/mês).
        var grupos = itens
            .GroupBy(i => (Cfop: i.CfopSnapshot, Cst: i.CstOuCsosn, Ncm: i.NcmSnapshot))
            .OrderBy(g => g.Key.Cfop)
            .ThenBy(g => g.Key.Cst)
            .ThenBy(g => g.Key.Ncm);

        foreach (var grupo in grupos)
        {
            var temTributos = grupo.Any(i => i.BaseIcms.HasValue);

            yield return new TotalizadoresFiscaisRow(
                Cfop:               grupo.Key.Cfop,
                CstOuCsosn:         grupo.Key.Cst,
                Ncm:                grupo.Key.Ncm,
                QtdItens:           grupo.Count(),
                TotalItens:         grupo.Sum(i => i.Subtotal.Valor),
                BaseIcms:           grupo.Sum(i => i.BaseIcms ?? 0m),
                ValorIcms:          grupo.Sum(i => i.ValorIcms ?? 0m),
                Pis:                grupo.Sum(i => i.Pis ?? 0m),
                Cofins:             grupo.Sum(i => i.Cofins ?? 0m),
                TributosRastreados: temTributos);
        }
    }

    public TotalizadoresFiscaisParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<TotalizadoresFiscaisParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar TotalizadoresFiscaisParams.");
}
