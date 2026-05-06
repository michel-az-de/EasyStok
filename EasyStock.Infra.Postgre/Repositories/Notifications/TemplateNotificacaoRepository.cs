using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class TemplateNotificacaoRepository(EasyStockDbContext db) : ITemplateRepository
{
    public Task<TemplateNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotifTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TemplateNotificacao?> GetAtivoAsync(
        string codigo, CanalNotificacao canal, Guid? empresaId, CancellationToken ct = default)
    {
        return await db.NotifTemplates
            .AsNoTracking()
            .Where(t => t.Codigo == codigo && t.Canal == canal && t.Ativo && t.Aprovado
                        && t.EmpresaId == empresaId)
            .OrderByDescending(t => t.Versao)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<(IReadOnlyList<TemplateNotificacao> Items, int TotalCount)> ListarAsync(
        Guid? empresaId, TipoEventoNotificacao? tipoEvento = null,
        CanalNotificacao? canal = null, bool? ativo = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var q = db.NotifTemplates.AsNoTracking()
            .Where(t => t.EmpresaId == empresaId);

        if (tipoEvento.HasValue) q = q.Where(t => t.TipoEvento == tipoEvento);
        if (canal.HasValue) q = q.Where(t => t.Canal == canal);
        if (ativo.HasValue) q = q.Where(t => t.Ativo == ativo);

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(t => t.AtualizadoEm)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return ((IReadOnlyList<TemplateNotificacao>)items, total);
    }

    public async Task AddAsync(TemplateNotificacao template, CancellationToken ct = default) =>
        await db.NotifTemplates.AddAsync(template, ct);

    public Task UpdateAsync(TemplateNotificacao template, CancellationToken ct = default)
    {
        db.NotifTemplates.Update(template);
        return Task.CompletedTask;
    }
}
