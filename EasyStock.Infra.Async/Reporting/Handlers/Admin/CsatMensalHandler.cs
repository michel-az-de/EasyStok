using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Admin.Tickets;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Admin;

/// <summary>
/// Handler do relatório de CSAT mensal — Admin SaaS.
/// Tickets com convite de satisfação enviado ou avaliados no período.
/// </summary>
public sealed class CsatMensalHandler(EasyStockDbContext db)
    : IReportHandler<CsatMensalParams, CsatMensalRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(CsatMensalParams parametros)
    {
        var fileNameBase = $"csat-mensal_{parametros.De:yyyy-MM-dd}_a_{parametros.Ate:yyyy-MM-dd}";
        return new ReportSchema(
            title:        "CSAT mensal",
            fileNameBase: fileNameBase,
            columns:
            [
                new("TicketId",         "ID do ticket",         0),
                new("EmpresaNome",      "Empresa",              1),
                new("Categoria",        "Categoria",            2),
                new("Prioridade",       "Prioridade",           3),
                new("CriadoEm",         "Criado em",            4, "dd/MM/yyyy HH:mm"),
                new("ResolvidoEm",      "Resolvido em",         5, "dd/MM/yyyy HH:mm"),
                new("ConviteEnviado",   "Convite enviado",      6),
                new("ConviteEnviadoEm", "Convite enviado em",   7, "dd/MM/yyyy HH:mm"),
                new("NotaCsat",         "Nota CSAT",            8),
                new("AvaliadoEm",       "Avaliado em",          9, "dd/MM/yyyy HH:mm"),
            ]);
    }

    public Task ValidateAsync(CsatMensalParams parametros, CancellationToken ct)
    {
        if (parametros.De > parametros.Ate)
            throw new ArgumentException(
                "A data final deve ser igual ou posterior à inicial.",
                nameof(parametros.Ate));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<CsatMensalRow> StreamAsync(
        CsatMensalParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var de  = parametros.De.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ate = parametros.Ate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var query = db.AdminTickets
            .IgnoreQueryFilters()
            .Where(t => (t.ConviteCsatEnviadoEm != null || t.NotaCsat != null)
                     && t.CriadoEm >= de && t.CriadoEm <= ate)
            .OrderBy(t => t.CriadoEm)
            .Select(t => new
            {
                t.Id,
                t.Categoria,
                t.Prioridade,
                t.CriadoEm,
                t.ResolvidoEm,
                t.NotaCsat,
                t.AvaliadoEm,
                t.ConviteCsatEnviadoEm,
                EmpresaNome = db.Empresas
                    .Where(e => e.Id == t.EmpresaId)
                    .Select(e => e.Nome)
                    .FirstOrDefault() ?? "—",
            })
            .AsNoTracking()
            .AsAsyncEnumerable();

        await foreach (var t in query.WithCancellation(ct))
        {
            yield return new CsatMensalRow(
                TicketId:         t.Id,
                EmpresaNome:      t.EmpresaNome,
                Categoria:        t.Categoria.ToString(),
                Prioridade:       t.Prioridade.ToString(),
                CriadoEm:         t.CriadoEm,
                ResolvidoEm:      t.ResolvidoEm,
                NotaCsat:         t.NotaCsat,
                AvaliadoEm:       t.AvaliadoEm,
                ConviteEnviado:   t.ConviteCsatEnviadoEm.HasValue,
                ConviteEnviadoEm: t.ConviteCsatEnviadoEm);
        }
    }

    public CsatMensalParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<CsatMensalParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar CsatMensalParams.");
}
