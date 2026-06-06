using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    /// <summary>
    /// Acesso admin a tickets. Uso interno por use cases admin (back-office).
    /// </summary>
    public sealed class AdminTicketRepository(EasyStockDbContext db) : IAdminTicketRepository
    {
        public async Task<AdminTicket?> ObterPorIdAsync(Guid id, Guid empresaId, CancellationToken ct = default)
        {
            return await db.AdminTickets
                .Include(t => t.Mensagens)
                .Include(t => t.CriadoPor)
                .Include(t => t.Atendente)
                .Include(t => t.Empresa)
                .FirstOrDefaultAsync(t => t.Id == id && t.EmpresaId == empresaId, ct);
        }

        public async Task<AdminTicket?> ObterPorIdGlobalAsync(Guid id, CancellationToken ct = default)
        {
            // ignora filtro tenant — usado por superadmin/jobs
            return await db.AdminTickets
                .IgnoreQueryFilters()
                .Include(t => t.Mensagens)
                .Include(t => t.CriadoPor)
                .Include(t => t.Atendente)
                .Include(t => t.Empresa)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
        }

        public async Task<(IReadOnlyList<AdminTicket> Itens, int Total)> ListarAsync(
            AdminTicketFiltro filtro,
            CancellationToken ct = default)
        {
            var page = Math.Max(1, filtro.Page);
            var pageSize = Math.Clamp(filtro.PageSize, 1, 100);

            var query = db.AdminTickets.AsNoTracking().AsQueryable();

            if (filtro.EmpresaId.HasValue)
                query = query.Where(t => t.EmpresaId == filtro.EmpresaId.Value);

            if (filtro.Status.HasValue)
                query = query.Where(t => t.Status == filtro.Status.Value);

            if (filtro.Categoria.HasValue)
                query = query.Where(t => t.Categoria == filtro.Categoria.Value);

            if (filtro.Prioridade.HasValue)
                query = query.Where(t => t.Prioridade == filtro.Prioridade.Value);

            if (filtro.Nivel.HasValue)
                query = query.Where(t => t.Nivel == filtro.Nivel.Value);

            if (filtro.AtendenteId.HasValue)
                query = query.Where(t => t.AtendenteId == filtro.AtendenteId.Value);

            if (filtro.SlaViolado.HasValue && filtro.SlaViolado.Value)
                query = query.Where(t => t.SlaRespostaViolado || t.SlaResolucaoViolado);

            var total = await query.CountAsync(ct);
            var itens = await query
                .OrderByDescending(t => t.Prioridade)
                .ThenByDescending(t => t.CriadoEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (itens, total);
        }

        public async Task<IReadOnlyList<TicketExportRow>> ListarParaExportarAsync(
            AdminTicketExportFiltro filtro, CancellationToken ct = default)
        {
            var query = db.AdminTickets.AsNoTracking().AsQueryable();

            // Ids selecionados na tela tem precedencia sobre os filtros.
            if (filtro.Ids is { Count: > 0 })
            {
                var ids = filtro.Ids;
                query = query.Where(t => ids.Contains(t.Id));
            }
            else
            {
                // Mesmas clausulas Where de AdminTicketsController.GetTickets — paridade de filtro.
                if (filtro.Status.HasValue)
                    query = query.Where(t => t.Status == filtro.Status.Value);
                if (filtro.Prioridade.HasValue)
                    query = query.Where(t => t.Prioridade == filtro.Prioridade.Value);
                if (filtro.Nivel.HasValue)
                    query = query.Where(t => t.Nivel == filtro.Nivel.Value);
                if (filtro.Categoria.HasValue)
                    query = query.Where(t => t.Categoria == filtro.Categoria.Value);
                if (filtro.EmpresaId.HasValue)
                    query = query.Where(t => t.EmpresaId == filtro.EmpresaId.Value);
                if (filtro.AtendenteId.HasValue)
                    query = query.Where(t => t.AtendenteId == filtro.AtendenteId.Value);
                if (!string.IsNullOrWhiteSpace(filtro.SlaStatus))
                {
                    query = filtro.SlaStatus.ToLowerInvariant() switch
                    {
                        "violado" => query.Where(t => t.SlaRespostaViolado || t.SlaResolucaoViolado),
                        "ok" => query.Where(t => !t.SlaRespostaViolado && !t.SlaResolucaoViolado),
                        _ => query
                    };
                }
                if (!string.IsNullOrWhiteSpace(filtro.Search))
                    query = query.Where(t => t.Titulo.Contains(filtro.Search));
            }

            var limite = Math.Clamp(filtro.Limite, 1, 10000);

            // Ordem da tela (GetTickets): violado, prazo proximo, criado — + Id de desempate.
            return await query
                .OrderByDescending(t => t.SlaRespostaViolado || t.SlaResolucaoViolado)
                .ThenBy(t => t.PrazoResposta ?? DateTime.MaxValue)
                .ThenByDescending(t => t.CriadoEm)
                .ThenBy(t => t.Id)
                .Take(limite)
                .Select(t => new TicketExportRow(
                    t.Titulo,
                    t.Empresa != null ? t.Empresa.Nome : null,
                    t.Categoria,
                    t.Prioridade,
                    t.Nivel,
                    t.Status,
                    t.Atendente != null ? t.Atendente.Nome : null,
                    t.CriadoEm,
                    t.PrazoResposta,
                    t.PrazoResolucao,
                    t.SlaRespostaViolado,
                    t.SlaResolucaoViolado,
                    t.ResolvidoEm,
                    t.NotaCsat))
                .ToListAsync(ct);
        }

        public async Task InserirAsync(AdminTicket ticket, CancellationToken ct = default)
        {
            await db.AdminTickets.AddAsync(ticket, ct);
        }

        public Task AtualizarAsync(AdminTicket ticket, CancellationToken ct = default)
        {
            db.AdminTickets.Update(ticket);
            return Task.CompletedTask;
        }
    }
}
