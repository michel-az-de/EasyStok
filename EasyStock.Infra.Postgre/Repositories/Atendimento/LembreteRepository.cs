using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class LembreteRepository(EasyStockDbContext db) : ILembreteRepository
{
    public Task AddAsync(Lembrete lembrete, CancellationToken ct = default) =>
        db.Lembretes.AddAsync(lembrete, ct).AsTask();

    public Task<Lembrete?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.Lembretes.FirstOrDefaultAsync(l => l.EmpresaId == empresaId && l.Id == id, ct);

    public async Task<IReadOnlyList<Lembrete>> ListarAsync(
        Guid empresaId, Guid? usuarioId, bool incluirConcluidos, int limite, CancellationToken ct = default)
    {
        var query = db.Lembretes.AsNoTracking().Where(l => l.EmpresaId == empresaId);
        if (!incluirConcluidos) query = query.Where(l => l.Situacao == SituacaoLembrete.Aberto);
        if (usuarioId is { } u) query = query.Where(l => l.ParaUsuarioId == null || l.ParaUsuarioId == u);
        return await query.OrderByDescending(l => l.VenceEm).Take(Math.Clamp(limite, 1, 500)).ToListAsync(ct);
    }

    public Task<int> MarcarVencidosVistosAsync(Guid empresaId, Guid usuarioId, DateTime agoraUtc, CancellationToken ct = default) =>
        db.Lembretes
            .Where(l => l.EmpresaId == empresaId && (l.ParaUsuarioId == null || l.ParaUsuarioId == usuarioId)
                && l.Situacao == SituacaoLembrete.Aberto && l.VistoEm == null && l.VenceEm <= agoraUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.VistoEm, agoraUtc), ct);

    public async Task<IReadOnlyCollection<(Guid EmpresaId, string Referencia)>> ListarAutomaticosExistentesAsync(
        TipoLembrete tipo, IReadOnlyCollection<(Guid EmpresaId, string Referencia)> fatos, CancellationToken ct = default)
    {
        if (fatos.Count == 0) return [];
        // O banco filtra por empresa e por referência (o índice único começa pela empresa); o par exato é conferido aqui.
        var empresas = fatos.Select(f => f.EmpresaId).Distinct().ToList();
        var referencias = fatos.Select(f => f.Referencia).Distinct().ToList();
        var procurados = fatos.ToHashSet();
        var achados = await db.Lembretes.IgnoreQueryFilters()
            .Where(l => empresas.Contains(l.EmpresaId) && l.Tipo == tipo && l.Referencia != null && referencias.Contains(l.Referencia))
            .Select(l => new { l.EmpresaId, l.Referencia })
            .ToListAsync(ct);
        return achados.Select(a => (a.EmpresaId, a.Referencia!)).Where(procurados.Contains).ToHashSet();
    }

    // Cross-tenant (avaliador, com bypass de RLS): IgnoreQueryFilters tira o filtro global de tenant.
    public async Task<IReadOnlyList<Lembrete>> ListarAutomaticosAbertosAsync(CancellationToken ct = default) =>
        await db.Lembretes.IgnoreQueryFilters()
            .Where(l => l.Situacao == SituacaoLembrete.Aberto && l.Tipo != TipoLembrete.Manual)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Lembrete>> ListarVencidosSemAvisoAsync(DateTime agoraUtc, int limite, CancellationToken ct = default) =>
        await db.Lembretes.IgnoreQueryFilters()
            .Where(l => l.Situacao == SituacaoLembrete.Aberto && l.AvisadoEm == null && l.VenceEm <= agoraUtc)
            .OrderBy(l => l.VenceEm)
            .Take(Math.Clamp(limite, 1, 500))
            .ToListAsync(ct);
}
