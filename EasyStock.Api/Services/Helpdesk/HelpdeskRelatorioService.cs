using System.Globalization;
using System.Text;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

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

        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        sb.AppendLine("id;titulo;categoria;prioridade;status;criado_em;resolvido_em;tempo_resolucao_horas;sla_atendido;nota_csat;atendente");

        foreach (var l in linhas)
        {
            var tempoHoras = l.ResolvidoEm.HasValue
                ? Math.Round((l.ResolvidoEm.Value - l.CriadoEm).TotalHours, 2).ToString(inv)
                : "";
            // sla_atendido so faz sentido para tickets resolvidos. Sem ResolvidoEm
            // deixamos vazio para nao mascarar como "atendido" prematuramente.
            var slaAtendido = l.ResolvidoEm.HasValue
                ? (l.SlaResolucaoViolado ? "nao" : "sim")
                : "";

            sb.Append(l.Id).Append(';');
            sb.Append(EscapeCsv(l.Titulo)).Append(';');
            sb.Append(l.Categoria).Append(';');
            sb.Append(l.Prioridade).Append(';');
            sb.Append(l.Status).Append(';');
            sb.Append(l.CriadoEm.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append(';');
            sb.Append(l.ResolvidoEm?.ToString("yyyy-MM-dd HH:mm:ss", inv) ?? "").Append(';');
            sb.Append(tempoHoras).Append(';');
            sb.Append(slaAtendido).Append(';');
            sb.Append(l.NotaCsat?.ToString(inv) ?? "").Append(';');
            sb.Append(EscapeCsv(l.AtendenteNome ?? ""));
            sb.AppendLine();
        }

        // BOM UTF-8 — Excel/Google Sheets reconhece encoding correto.
        var bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var output = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, output, 0, bom.Length);
        Buffer.BlockCopy(body, 0, output, bom.Length, body.Length);
        return output;
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
