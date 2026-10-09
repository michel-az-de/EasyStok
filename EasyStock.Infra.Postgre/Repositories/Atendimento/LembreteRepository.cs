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

    public Task<bool> ExisteAutomaticoAsync(Guid empresaId, TipoLembrete tipo, string referencia, CancellationToken ct = default) =>
        db.Lembretes.IgnoreQueryFilters()
            .AnyAsync(l => l.EmpresaId == empresaId && l.Tipo == tipo && l.Referencia == referencia, ct);

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
