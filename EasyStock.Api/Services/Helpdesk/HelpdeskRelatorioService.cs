using System.Globalization;
using EasyStock.Application.Common;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Services.Helpdesk;

/// <summary>
/// Gera relatorios exportaveis de helpdesk filtrados por empresa.
/// CSV em UTF-8 com BOM (Excel-friendly), separador ; (locale pt-BR).
/// </summary>
public sealed class HelpdeskRelatorioService(EasyStockDbContext db)
{
    public async Task<byte[]> GerarCsvAsync(
        Guid empresaId,
        DateTime de,
        DateTime ate,
        CancellationToken ct = default)
    {
        if (de > ate)
            throw new InvalidOperationException("Periodo invalido: de > ate.");

        var deUtc = DateTime.SpecifyKind(de.Date, DateTimeKind.Utc);
        var ateUtc = DateTime.SpecifyKind(ate.Date.AddDays(1), DateTimeKind.Utc);

        var linhas = await db.AdminTickets
            .AsNoTracking()
            .Where(t => t.EmpresaId == empresaId
                && t.CriadoEm >= deUtc
                && t.CriadoEm < ateUtc)
            .OrderBy(t => t.CriadoEm)
            .Select(t => new
            {
                t.Id,
                t.Titulo,
                Categoria = t.Categoria.ToString(),
                Prioridade = t.Prioridade.ToString(),
                Status = t.Status.ToString(),
                t.CriadoEm,
                t.ResolvidoEm,
                t.SlaResolucaoViolado,
                t.NotaCsat,
                AtendenteNome = t.Atendente == null ? null : t.Atendente.Nome
            })
            .ToListAsync(ct);

        var inv = CultureInfo.InvariantCulture;

        var headers = new[]
        {
            "id", "titulo", "categoria", "prioridade", "status", "criado_em", "resolvido_em",
            "tempo_resolucao_horas", "sla_atendido", "nota_csat", "atendente"
        };

        var rows = linhas.Select(l =>
        {
            var tempoHoras = l.ResolvidoEm.HasValue
                ? Math.Round((l.ResolvidoEm.Value - l.CriadoEm).TotalHours, 2).ToString(inv)
                : "";
            // sla_atendido so faz sentido para tickets resolvidos. Sem ResolvidoEm
            // deixamos vazio para nao mascarar como "atendido" prematuramente.
            var slaAtendido = l.ResolvidoEm.HasValue
                ? (l.SlaResolucaoViolado ? "nao" : "sim")
                : "";

            return new[]
            {
                l.Id.ToString(),
                l.Titulo ?? "",
                l.Categoria,
                l.Prioridade,
                l.Status,
                l.CriadoEm.ToString("yyyy-MM-dd HH:mm:ss", inv),
                l.ResolvidoEm?.ToString("yyyy-MM-dd HH:mm:ss", inv) ?? "",
                tempoHoras,
                slaAtendido,
                l.NotaCsat?.ToString(inv) ?? "",
                l.AtendenteNome ?? ""
            };
        });

        // CSV central (#612): BOM UTF-8, separador ';', anti-injecao de formula + quoting RFC-4180.
        return Csv.Build(headers, rows);
    }
}
