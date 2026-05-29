using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "SuperAdmin")]
public class AdminDashboardController(EasyStockDbContext db) : EasyStockControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var agora = DateTime.UtcNow;
        var h24 = agora.AddHours(-24);
        var d30 = agora.AddDays(-30);

        var totalTenants = await db.Empresas.CountAsync();

        var tenantsAtivos = await db.AssinaturasEmpresa.CountAsync(a => a.Status == StatusAssinatura.Ativa);
        var tenantsSuspensos = await db.AssinaturasEmpresa.CountAsync(a => a.Status == StatusAssinatura.Suspensa);
        var receitaMensal = await db.AssinaturasEmpresa
            .Where(a => a.Status == StatusAssinatura.Ativa && a.Plano != null)
            .SumAsync(a => a.Plano!.PrecoMensal);

        var tenantsNovos = await db.Empresas.CountAsync(e => e.CriadoEm >= d30);

        var ticketsAbertos = await db.AdminTickets.CountAsync(t => t.Status == TicketStatus.Aberto);
        var ticketsCriticos = await db.AdminTickets.CountAsync(t =>
            t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Resolvido
            && t.Prioridade == TicketPrioridade.Critica);
        var ticketsEmAtendimento = await db.AdminTickets.CountAsync(t => t.Status == TicketStatus.EmAtendimento);

        var totalUsuariosAtivos = await db.Usuarios.CountAsync(u => u.Ativo);
        var logins24h = await db.AuditLogs.CountAsync(a => a.DataHora >= h24 && a.Acao == "Login" && a.Sucesso);

        var ticketsComNovaMensagem = await db.AdminTickets
            .CountAsync(t => t.Status != TicketStatus.Fechado && t.Status != TicketStatus.Resolvido
                && db.AdminTicketMensagens.Any(m => m.TicketId == t.Id && !m.IsAdmin && !m.LidoPeloAdmin));

        var ultimosTicketsCriticos = await db.AdminTickets
            .Include(t => t.Empresa)
            .Where(t => t.Prioridade == TicketPrioridade.Critica && t.Status != TicketStatus.Fechado)
            .OrderByDescending(t => t.CriadoEm)
            .Take(5)
            .Select(t => new
            {
                t.Id,
                t.Titulo,
                empresaNome = t.Empresa == null ? "" : t.Empresa.Nome,
                status = t.Status.ToString(),
                prioridade = t.Prioridade.ToString(),
                t.CriadoEm
            })
            .ToListAsync();

        var tenantsRecentes = await db.Empresas
            .Where(e => e.CriadoEm >= d30)
            .OrderByDescending(e => e.CriadoEm)
            .Take(5)
            .Select(e => new { e.Id, e.Nome, e.Documento, e.CriadoEm })
            .ToListAsync();

        return DataOk(new
        {
            totalTenants,
            tenantsAtivos,
            tenantsSuspensos,
            tenantsNovosUltimos30Dias = tenantsNovos,
            ticketsAbertos,
            ticketsCriticos,
            ticketsEmAtendimento,
            ticketsComNovaMensagem,
            totalUsuariosAtivos,
            logins24h,
            receitaMensalEstimada = receitaMensal,
            ultimosTicketsCriticos,
            tenantsRecentes
        });
    }
}
