using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre do read model do dashboard Admin (F7).
///
/// <para>
/// Leitura SuperAdmin cross-tenant: abre <see cref="EasyStockDbContext.UseRowLevelSecurityBypass"/>
/// no escopo inteiro (espelha <see cref="FleetOperationQueries"/>). Sem isso, sob role sem
/// BYPASSRLS (prod, <c>easystok_user</c>) a policy do Postgres zera as linhas com EmpresaId e
/// as métricas agregadas (MRR, tickets, etc.) viriam truncadas — o bug F2 do ADR-0037 (adendo,
/// issue #754). O MRR reusa o predicado canônico
/// <see cref="IAssinaturaEmpresaRepository.SomarPrecoMensalAtivasAsync"/> (fonte única de
/// definição, mesma de <c>MetricasFinanceirasUseCase</c> e <c>RevenueMetricsQueries</c>).
/// </para>
/// </summary>
public sealed class AdminDashboardQueries(
    EasyStockDbContext db,
    IAssinaturaEmpresaRepository assinaturaRepo) : IAdminDashboardQueries
{
    public async Task<AdminDashboardData> ObterAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        // Leitura cross-tenant deliberada (SuperAdmin) — abre o RLS p/ não truncar as métricas
        // agregadas sob role sem BYPASSRLS. Setado ANTES de qualquer query, igual FleetOperationQueries.
        using var _ = db.UseRowLevelSecurityBypass();

        var h24 = nowUtc.AddHours(-24);
        var d30 = nowUtc.AddDays(-30);

        var totalTenants = await db.Empresas.CountAsync(ct);

        var tenantsAtivos = await db.AssinaturasEmpresa.CountAsync(a => a.Status == StatusAssinatura.Ativa, ct);
        var tenantsSuspensos = await db.AssinaturasEmpresa.CountAsync(a => a.Status == StatusAssinatura.Suspensa, ct);
        // MRR = predicado canônico único (Ativa → SUM PrecoMensal), sob o bypass acima.
        var receitaMensal = await assinaturaRepo.SomarPrecoMensalAtivasAsync(null, ct);

        var tenantsNovos = await db.Empresas.CountAsync(e => e.CriadoEm >= d30, ct);

        // "Em aberto" = predicado canônico compartilhado (issue 692): tudo que não é
        // Resolvido nem Fechado. Mantém paridade com Diagnóstico e Operação/Fleet.
        var ticketsAbertos = await db.AdminTickets.CountAsync(AdminTicketFilters.EmAberto, ct);
        var ticketsCriticos = await db.AdminTickets.CountAsync(t =>
            t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Resolvido
            && t.Prioridade == TicketPrioridade.Critica, ct);
        var ticketsEmAtendimento = await db.AdminTickets.CountAsync(t => t.Status == TicketStatus.EmAtendimento, ct);

        // "Usuários ativos" = usuários ativos vinculados a algum tenant (espelha a contagem
        // por-tenant). Exclui SuperAdmin/OPS internos, que não têm vínculo UsuarioEmpresa e
        // por isso inflavam o número do dashboard sem aparecer na soma dos tenants (ADM-005, issue 695).
        var totalUsuariosAtivos = await db.Usuarios.CountAsync(u => u.Ativo && u.Empresas.Any(), ct);
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
