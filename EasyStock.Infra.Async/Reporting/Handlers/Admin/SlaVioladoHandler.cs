using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Admin.Tickets;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Admin;

/// <summary>
/// Handler do relatório de SLA violado — Admin SaaS.
/// Tickets com prazo de resposta ou resolução excedido no período.
/// </summary>
public sealed class SlaVioladoHandler(EasyStockDbContext db)
    : IReportHandler<SlaVioladoParams, SlaVioladoRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(SlaVioladoParams parametros)
    {
        var fileNameBase = $"sla-violado_{parametros.De:yyyy-MM-dd}_a_{parametros.Ate:yyyy-MM-dd}";
        return new ReportSchema(
            title:        "Tickets com SLA violado",
            fileNameBase: fileNameBase,
            columns:
            [
                new("TicketId",               "ID do ticket",               0),
                new("Titulo",                 "Título",                     1),
                new("EmpresaNome",            "Empresa",                    2),
                new("Categoria",              "Categoria",                  3),
                new("Prioridade",             "Prioridade",                 4),
                new("CriadoEm",               "Criado em",                  5, "dd/MM/yyyy HH:mm"),
                new("SlaRespostaViolado",     "SLA resposta violado",       6),
                new("SlaResolucaoViolado",    "SLA resolução violado",      7),
                new("PrazoResposta",          "Prazo resposta",             8, "dd/MM/yyyy HH:mm"),
                new("PrazoResolucao",         "Prazo resolução",            9, "dd/MM/yyyy HH:mm"),
                new("PrimeiraRespostaEm",     "Primeira resposta em",      10, "dd/MM/yyyy HH:mm"),
                new("ResolvidoEm",            "Resolvido em",              11, "dd/MM/yyyy HH:mm"),
                new("MinutosAtrasoResposta",  "Atraso resposta (min)",     12),
                new("MinutosAtrasoResolucao", "Atraso resolução (min)",    13),
            ]);
    }

    public Task ValidateAsync(SlaVioladoParams parametros, CancellationToken ct)
    {
        if (parametros.De > parametros.Ate)
            throw new ArgumentException(
                "A data final deve ser igual ou posterior à inicial.",
                nameof(parametros.Ate));
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<SlaVioladoRow> StreamAsync(
        SlaVioladoParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var de  = parametros.De.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var ate = parametros.Ate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var query = db.AdminTickets
            .IgnoreQueryFilters()
            .Where(t => (t.SlaRespostaViolado || t.SlaResolucaoViolado)
                     && t.CriadoEm >= de && t.CriadoEm <= ate)
            .OrderBy(t => t.CriadoEm)
            .Select(t => new
            {
                t.Id,
                t.Titulo,
                t.Categoria,
                t.Prioridade,
                t.CriadoEm,
                t.SlaRespostaViolado,
                t.SlaResolucaoViolado,
                t.PrazoResposta,
                t.PrazoResolucao,
                t.PrimeiraRespostaEm,
                t.ResolvidoEm,
                EmpresaNome = db.Empresas
                    .Where(e => e.Id == t.EmpresaId)
                    .Select(e => e.Nome)
                    .FirstOrDefault() ?? "—",
            })
            .AsNoTracking()
            .AsAsyncEnumerable();

        await foreach (var t in query.WithCancellation(ct))
        {
            var minutosAtrasoResposta = 0;
            if (t.SlaRespostaViolado && t.PrazoResposta.HasValue && t.PrimeiraRespostaEm.HasValue)
                minutosAtrasoResposta = (int)(t.PrimeiraRespostaEm.Value - t.PrazoResposta.Value).TotalMinutes;
            else if (t.SlaRespostaViolado && t.PrazoResposta.HasValue)
                minutosAtrasoResposta = (int)(DateTime.UtcNow - t.PrazoResposta.Value).TotalMinutes;

            var minutosAtrasoResolucao = 0;
            if (t.SlaResolucaoViolado && t.PrazoResolucao.HasValue && t.ResolvidoEm.HasValue)
                minutosAtrasoResolucao = (int)(t.ResolvidoEm.Value - t.PrazoResolucao.Value).TotalMinutes;
            else if (t.SlaResolucaoViolado && t.PrazoResolucao.HasValue)
                minutosAtrasoResolucao = (int)(DateTime.UtcNow - t.PrazoResolucao.Value).TotalMinutes;

            yield return new SlaVioladoRow(
                TicketId:               t.Id,
                Titulo:                 t.Titulo,
                EmpresaNome:            t.EmpresaNome,
                Categoria:              t.Categoria.ToString(),
                Prioridade:             t.Prioridade.ToString(),
                CriadoEm:               t.CriadoEm,
                SlaRespostaViolado:     t.SlaRespostaViolado,
                SlaResolucaoViolado:    t.SlaResolucaoViolado,
                PrazoResposta:          t.PrazoResposta,
                PrazoResolucao:         t.PrazoResolucao,
                PrimeiraRespostaEm:     t.PrimeiraRespostaEm,
                ResolvidoEm:            t.ResolvidoEm,
                MinutosAtrasoResposta:  minutosAtrasoResposta,
                MinutosAtrasoResolucao: minutosAtrasoResolucao);
        }
    }

    public SlaVioladoParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<SlaVioladoParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar SlaVioladoParams.");
}
