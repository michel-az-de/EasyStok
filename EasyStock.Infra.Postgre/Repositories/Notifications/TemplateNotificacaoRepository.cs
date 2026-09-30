using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class TemplateNotificacaoRepository(EasyStockDbContext db) : ITemplateRepository
{
    public Task<TemplateNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.NotifTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TemplateNotificacao?> GetAtivoAsync(
        string codigo, CanalNotificacao canal, Guid? empresaId, CancellationToken ct = default)
    {
        if (empresaId is null)
            return await GetGlobalAtivoAsync(codigo, canal, ct);

        return await db.NotifTemplates
            .AsNoTracking()
            .Where(t => t.Codigo == codigo && t.Canal == canal && t.Ativo && t.Aprovado
                        && t.EmpresaId == empresaId)
            .OrderByDescending(t => t.Versao)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Template global (<c>EmpresaId</c> nulo). No escopo de uma empresa, o filtro do EF e a policy
    /// <c>tenant_isolation</c> (<c>EmpresaId = app.empresa_id</c>) o escondem, e o fallback para o
    /// global nunca o achava (S30, provado em <c>DisparoCampanhaIntegrationTests</c>). A leitura
    /// abre conexão própria com o bypass de RLS e o WHERE só alcança linhas globais. Com a conexão
    /// já aberta por quem chama, o bypass não se aplica e vale o comportamento anterior.
    /// </summary>
    private async Task<TemplateNotificacao?> GetGlobalAtivoAsync(string codigo, CanalNotificacao canal, CancellationToken ct)
    {
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.NotifTemplates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Codigo == codigo && t.Canal == canal && t.Ativo && t.Aprovado && t.EmpresaId == null)
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
