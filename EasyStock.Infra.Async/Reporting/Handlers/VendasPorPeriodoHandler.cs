using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.VendasPorPeriodo;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers;

/// <summary>
/// Handler do relatório "Vendas por período" (Tenant).
/// Usa <see cref="ITenantScopedQueryBuilder"/> para isolamento explícito por empresa (ADR-R07).
/// Streaming via <see cref="IAsyncEnumerable{T}"/> — sem materializar toda a coleção.
/// </summary>
public sealed class VendasPorPeriodoHandler(
    EasyStockDbContext       db,
    ITenantScopedQueryBuilder tenantQuery)
    : IReportHandler<VendasPorPeriodoParams, VendasPorPeriodoRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(VendasPorPeriodoParams parametros)
    {
        var fileNameBase = $"vendas-por-periodo_{parametros.De:yyyy-MM-dd}_a_{parametros.Ate:yyyy-MM-dd}";

        return new ReportSchema(
            title:        "Vendas por período",
            fileNameBase: fileNameBase,
            columns:
            [
                new("DataVenda",                "Data/Hora",                          0, "dd/MM/yyyy HH:mm:ss"),
                new("NumeroNotaFiscal",         "NF",                                 1),
                new("IdCurto",                  "ID (curto)",                         2),
                new("LojaNome",                 "Loja",                               3),
                new("VendedorNome",             "Vendedor",                           4),
                new("FormaPagamentoPrincipal",  "Forma de pagamento",                 5),
                new("QtdItens",                 "Qtd. itens",                         6),
                new("Subtotal",                 "Subtotal (R$)",                      7, "0.00"),
                new("ValorDesconto",            "Desconto (R$)",                      8, "0.00"),
                new("ValorTotal",               "Total (R$)",                         9, "0.00"),
            ]);
    }

    public async Task ValidateAsync(VendasPorPeriodoParams parametros, CancellationToken ct)
    {
        if (parametros.De > parametros.Ate)
            throw new ArgumentException(
                "A data final deve ser igual ou posterior à inicial.",
                nameof(parametros.Ate));

        if (parametros.Ate.DayNumber - parametros.De.DayNumber > 365 * 2)
            throw new ArgumentException(
                "Para períodos maiores que 24 meses, divida em gerações menores.",
                nameof(parametros.Ate));
    }

    public async IAsyncEnumerable<VendasPorPeriodoRow> StreamAsync(
        VendasPorPeriodoParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var de  = parametros.De.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var ate = parametros.Ate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);

        // ADR-R07: query explícita com WHERE EmpresaId; IgnoreQueryFilters() via tenantQuery.Query<T>()
        var query = tenantQuery.Query<EasyStock.Domain.Entities.Venda>()
            .Where(v => v.DataVenda >= de && v.DataVenda <= ate);

        if (parametros.LojaId.HasValue)
            query = query.Where(v => v.LojaId == parametros.LojaId.Value);

        if (parametros.FormaPagamento is { Length: > 0 } forma)
            query = query.Where(v => v.FormaPagamentoPrincipal == forma);

        if (parametros.VendedorId.HasValue)
            query = query.Where(v => v.VendedorId == parametros.VendedorId.Value);

        // Projeção com LEFT JOIN em Loja e Vendedor (via EF navigation ou subquery)
        var projected = query
            .OrderBy(v => v.DataVenda)
            .Select(v => new
            {
                v.Id,
                v.DataVenda,
                v.NumeroNotaFiscal,
                v.FormaPagamentoPrincipal,
                LojaNome      = v.LojaId == null ? null
                    : db.Lojas.Where(l => l.Id == v.LojaId).Select(l => l.Nome).FirstOrDefault(),
                VendedorNome  = v.VendedorId == null ? null
                    : db.Usuarios.Where(u => u.Id == v.VendedorId).Select(u => u.Nome).FirstOrDefault(),
                QtdItens      = db.ItensVenda.Count(i => i.VendaId == v.Id),
                Subtotal      = v.Subtotal != null ? v.Subtotal.Valor : v.ValorTotal.Valor,
                ValorDesconto = v.ValorDesconto != null ? v.ValorDesconto.Valor : 0m,
                ValorTotal    = v.ValorTotal.Valor,
            })
            .AsNoTracking()
            .AsAsyncEnumerable();

        await foreach (var r in projected.WithCancellation(ct))
        {
            yield return new VendasPorPeriodoRow(
                DataVenda:               r.DataVenda,
                NumeroNotaFiscal:        r.NumeroNotaFiscal,
                IdCurto:                 r.Id.ToString()[..8],
                LojaNome:                r.LojaNome,
                VendedorNome:            r.VendedorNome,
                FormaPagamentoPrincipal: r.FormaPagamentoPrincipal,
                QtdItens:                r.QtdItens,
                Subtotal:                r.Subtotal,
                ValorDesconto:           r.ValorDesconto,
                ValorTotal:              r.ValorTotal);
        }
    }

    public VendasPorPeriodoParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<VendasPorPeriodoParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar VendasPorPeriodoParams.");
}
