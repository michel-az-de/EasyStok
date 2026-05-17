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
/// Handler do relatório de inadimplência — Admin SaaS.
/// Faturas vencidas cross-tenant ordenadas por dias de atraso.
/// </summary>
public sealed class InadimplenciaHandler(EasyStockDbContext db)
    : IReportHandler<InadimplenciaParams, InadimplenciaRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(InadimplenciaParams parametros)
    {
        var fileNameBase = $"inadimplencia_{parametros.DataReferencia:yyyy-MM-dd}";
        return new ReportSchema(
            title:        "Inadimplência",
            fileNameBase: fileNameBase,
            columns:
            [
                new("EmpresaNome",    "Empresa",              0),
                new("FaturaNumero",   "Nº Fatura",            1),
                new("DataVencimento", "Vencimento",           2, "dd/MM/yyyy"),
                new("DiasAtraso",     "Dias em atraso",       3),
                new("ValorTotal",     "Total (R$)",           4, "0.00"),
                new("ValorPago",      "Valor pago (R$)",      5, "0.00"),
                new("SaldoDevedor",   "Saldo devedor (R$)",   6, "0.00"),
                new("StatusFatura",   "Status",               7),
            ]);
    }

    public Task ValidateAsync(InadimplenciaParams parametros, CancellationToken ct)
    {
        if (parametros.AtrasoMinimoEmDias < 0)
            throw new ArgumentException(
                "O atraso mínimo não pode ser negativo.",
                nameof(parametros.AtrasoMinimoEmDias));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<InadimplenciaRow> StreamAsync(
        InadimplenciaParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var dataRef = parametros.DataReferencia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var query = db.Faturas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(f => f.Status == StatusFatura.Vencida
                     && f.DataVencimento < dataRef)
            .OrderByDescending(f => dataRef - f.DataVencimento)
            .Select(f => new
            {
                f.Id,
                f.Numero,
                f.DataVencimento,
                f.Total,
                f.Status,
                EmpresaNome = db.Empresas
                    .Where(e => e.Id == f.EmpresaId)
                    .Select(e => e.Nome)
                    .FirstOrDefault() ?? "—",
                ValorPago = db.FaturaPagamentos
                    .Where(p => p.FaturaId == f.Id)
                    .Sum(p => (decimal?)p.Valor) ?? 0m,
            })
            .AsAsyncEnumerable();

        await foreach (var r in query.WithCancellation(ct))
        {
            var diasAtraso = (int)(dataRef - r.DataVencimento).TotalDays;

            if (diasAtraso < parametros.AtrasoMinimoEmDias)
                continue;

            var saldoDevedor = Math.Max(0m, r.Total - r.ValorPago);

            yield return new InadimplenciaRow(
                EmpresaNome:    r.EmpresaNome,
                FaturaNumero:   r.Numero,
                DataVencimento: r.DataVencimento,
                DiasAtraso:     diasAtraso,
                ValorTotal:     r.Total,
                ValorPago:      r.ValorPago,
                SaldoDevedor:   saldoDevedor,
                StatusFatura:   r.Status.ToString());
        }
    }

    public InadimplenciaParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<InadimplenciaParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar InadimplenciaParams.");
}
