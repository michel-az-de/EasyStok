using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Fiscal.LivroSaidas;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Fiscal;

/// <summary>
/// Handler do relatório "Livro de Saídas (NFC-e)" — Fase 2.
/// Retorna NFC-e com status Autorizada ou Cancelada no período.
/// NFC-e legadas (pré-PR-D) aparecem com tributos = R$ 0,00 e flag TributosRastreados=false.
/// </summary>
public sealed class LivroSaidasHandler(
    EasyStockDbContext        db,
    ITenantScopedQueryBuilder tenantQuery)
    : IReportHandler<LivroSaidasParams, LivroSaidasRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(LivroSaidasParams parametros)
    {
        var competencia = $"{parametros.De:yyyy-MM}";
        return new ReportSchema(
            title:        "Livro de Saídas (NFC-e)",
            fileNameBase: $"livro-saidas-nfce_{competencia}",
            columns:
            [
                new("DataAutorizacao",    "Data Autorização",       0, "dd/MM/yyyy HH:mm:ss"),
                new("Numero",             "Número",                 1),
                new("Serie",              "Série",                  2),
                new("ChaveAcesso",        "Chave de Acesso",        3),
                new("Status",             "Status",                 4),
                new("DestinatarioNome",   "Destinatário",           5),
                new("CfopPrincipal",      "CFOP Principal",         6),
                new("TotalNota",          "Total (R$)",             7, "0.00"),
                new("BaseIcms",           "Base ICMS (R$)",         8, "0.00"),
                new("ValorIcms",          "ICMS (R$)",              9, "0.00"),
                new("Pis",                "PIS (R$)",              10, "0.00"),
                new("Cofins",             "COFINS (R$)",           11, "0.00"),
                new("TributosRastreados", "Tributos rastreados?",  12),
            ]);
    }

    public async Task ValidateAsync(LivroSaidasParams parametros, CancellationToken ct)
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

    public async IAsyncEnumerable<LivroSaidasRow> StreamAsync(
        LivroSaidasParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var de  = parametros.De.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var ate = parametros.Ate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);

        // Livro fiscal: Autorizada + Cancelada com DataAutorizacao no período.
        var statusAutorizada = StatusNfe.Autorizada.ToString();
        var statusCancelada  = StatusNfe.Cancelada.ToString();

        var documentos = tenantQuery.Query<NfeDocumento>()
            .Where(n => (n.Status == StatusNfe.Autorizada || n.Status == StatusNfe.Cancelada)
                        && n.DataAutorizacao >= de && n.DataAutorizacao <= ate)
            .OrderBy(n => n.DataAutorizacao)
            .AsNoTracking()
            .AsAsyncEnumerable();

        await foreach (var doc in documentos.WithCancellation(ct))
        {
            // CFOP principal: derivado do item com maior subtotal no documento.
            // Carregado client-side para evitar GROUP BY complexo em streaming.
            var cfopPrincipal = await db.NfeItens
                .Where(i => i.NfeDocumentoId == doc.Id && i.CfopSnapshot != null)
                .OrderByDescending(i => i.Subtotal)
                .Select(i => i.CfopSnapshot)
                .FirstOrDefaultAsync(ct);

            // Tributos: agrega itens. NULL em todos os tributos = legada (pré-PR-D).
            var itens = await db.NfeItens
                .Where(i => i.NfeDocumentoId == doc.Id)
                .Select(i => new
                {
                    i.BaseIcms,
                    i.ValorIcms,
                    i.Pis,
                    i.Cofins,
                })
                .ToListAsync(ct);

            var temTributos   = itens.Any(i => i.BaseIcms.HasValue);
            var baseIcms      = itens.Sum(i => i.BaseIcms ?? 0m);
            var valorIcms     = itens.Sum(i => i.ValorIcms ?? 0m);
            var pis           = itens.Sum(i => i.Pis ?? 0m);
            var cofins        = itens.Sum(i => i.Cofins ?? 0m);

            yield return new LivroSaidasRow(
                DataAutorizacao:   doc.DataAutorizacao,
                Numero:            doc.Numero,
                Serie:             doc.Serie,
                ChaveAcesso:       doc.ChaveAcesso,
                Status:            doc.Status.ToString(),
                DestinatarioNome:  doc.DadosDestinatario?.Nome,
                CfopPrincipal:     cfopPrincipal,
                TotalNota:         doc.TotalNota.Valor,
                BaseIcms:          baseIcms,
                ValorIcms:         valorIcms,
                Pis:               pis,
                Cofins:            cofins,
                TributosRastreados: temTributos);
        }
    }

    public LivroSaidasParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<LivroSaidasParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar LivroSaidasParams.");
}
