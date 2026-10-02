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
    /// Template global (<c>EmpresaId</c> nulo). O filtro do EF o esconde de quem está no escopo de uma empresa e o
    /// fallback para o global nunca o achava (S30). Desde a N1 a policy <c>catalogo_global_leitura</c> o expõe ao
    /// tenant (só SELECT), então a leitura ignora o filtro do EF e o WHERE só alcança linhas globais, sem bypass de
    /// RLS: o repositório não liga mais a flag.
    /// </summary>
    private async Task<TemplateNotificacao?> GetGlobalAtivoAsync(string codigo, CanalNotificacao canal, CancellationToken ct)
    {
        return await db.NotifTemplates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Codigo == codigo && t.Canal == canal && t.Ativo && t.Aprovado && t.EmpresaId == null)
            .OrderByDescending(t => t.Versao)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Por tipo e canal (N5), com a mesma leitura do catálogo global da N1: o filtro do EF é ignorado e o predicado
    /// <c>EmpresaId == empresa ou nulo</c> delimita o que entra; a policy <c>catalogo_global_leitura</c> expõe os
    /// globais ao tenant (só SELECT), sem bypass de RLS. A empresa vem antes do global, depois a maior versão.
    /// </summary>
    public async Task<TemplateNotificacao?> GetAtivoPorTipoAsync(
        TipoEventoNotificacao tipo, CanalNotificacao canal, Guid? empresaId, CancellationToken ct = default)
    {
        return await db.NotifTemplates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.TipoEvento == tipo && t.Canal == canal && t.Ativo && t.Aprovado
                        && (t.EmpresaId == null || t.EmpresaId == empresaId))
            .OrderBy(t => t.EmpresaId == null)
            .ThenByDescending(t => t.Versao)
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
