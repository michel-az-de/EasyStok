using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre do read model do dashboard Admin (F7).
/// </summary>
public sealed class AdminDashboardQueries(EasyStockDbContext db) : IAdminDashboardQueries
{
    public async Task<AdminDashboardData> ObterAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var h24 = nowUtc.AddHours(-24);
        var d30 = nowUtc.AddDays(-30);

        var totalTenants = await db.Empresas.CountAsync(ct);

        var tenantsAtivos = await db.AssinaturasEmpresa.CountAsync(a => a.Status == StatusAssinatura.Ativa, ct);
        var tenantsSuspensos = await db.AssinaturasEmpresa.CountAsync(a => a.Status == StatusAssinatura.Suspensa, ct);
        var receitaMensal = await db.AssinaturasEmpresa
            .Where(a => a.Status == StatusAssinatura.Ativa && a.Plano != null)
            .SumAsync(a => a.Plano!.PrecoMensal, ct);

        var tenantsNovos = await db.Empresas.CountAsync(e => e.CriadoEm >= d30, ct);

        var ticketsAbertos = await db.AdminTickets.CountAsync(t => t.Status == TicketStatus.Aberto, ct);
        var ticketsCriticos = await db.AdminTickets.CountAsync(t =>
            t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Resolvido
            && t.Prioridade == TicketPrioridade.Critica, ct);
        var ticketsEmAtendimento = await db.AdminTickets.CountAsync(t => t.Status == TicketStatus.EmAtendimento, ct);

        var totalUsuariosAtivos = await db.Usuarios.CountAsync(u => u.Ativo, ct);
        var logins24h = await db.AuditLogs.CountAsync(a => a.DataHora >= h24 && a.Acao == "Login" && a.Sucesso, ct);

        var ticketsComNovaMensagem = await db.AdminTickets
            .CountAsync(t => t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Resolvido
                && db.AdminTicketMensagens.Any(m => m.TicketId == t.Id && !m.IsAdmin && !m.LidoPeloAdmin), ct);

        var ultimosTicketsCriticos = await db.AdminTickets
            .Include(t => t.Empresa)
            .Where(t => t.Prioridade == TicketPrioridade.Critica && t.Status != TicketStatus.Fechado)
            .OrderByDescending(t => t.CriadoEm)
            .Take(5)
            .Select(t => new DashboardTicketCritico(
                t.Id, t.Titulo, t.Empresa == null ? "" : t.Empresa.Nome,
                t.Status.ToString(), t.Prioridade.ToString(), t.CriadoEm))
            .ToListAsync(ct);

        var tenantsRecentes = await db.Empresas
            .Where(e => e.CriadoEm >= d30)
            .OrderByDescending(e => e.CriadoEm)
            .Take(5)
            .Select(e => new DashboardTenantRecente(e.Id, e.Nome, e.Documento, e.CriadoEm))
            .ToListAsync(ct);

        return new AdminDashboardData(
            totalTenants, tenantsAtivos, tenantsSuspensos, tenantsNovos,
            ticketsAbertos, ticketsCriticos, ticketsEmAtendimento, ticketsComNovaMensagem,
            totalUsuariosAtivos, logins24h, receitaMensal,
            ultimosTicketsCriticos, tenantsRecentes);
    }
}
