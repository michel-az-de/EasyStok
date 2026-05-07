using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class ConsentimentoRepository(EasyStockDbContext db) : IConsentimentoRepository
{
    public Task<ConsentimentoNotificacao?> GetAsync(
        Guid usuarioId, CanalNotificacao canal, CategoriaConteudoNotificacao categoria,
        CancellationToken ct = default) =>
        db.NotifConsentimentos
            .AsNoTracking()
            .Where(c => c.UsuarioId == usuarioId && c.Canal == canal && c.Categoria == categoria)
            .OrderByDescending(c => c.AtualizadoEm)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ConsentimentoNotificacao>> ListarPorUsuarioAsync(
        Guid usuarioId, CancellationToken ct = default)
    {
        // Carrega todos os registros do usuário e agrupa em memória.
        // Cada mudança gera um novo registro (histórico imutável); o mais recente é o estado atual.
        // Max 12 registros por usuário (4 canais × 3 categorias) — in-memory é seguro.
        var all = await db.NotifConsentimentos
            .AsNoTracking()
            .Where(c => c.UsuarioId == usuarioId)
            .OrderByDescending(c => c.AtualizadoEm)
            .ToListAsync(ct);

        return all
            .GroupBy(c => new { c.Canal, c.Categoria })
            .Select(g => g.First())
            .ToList();
    }

    public async Task AddAsync(ConsentimentoNotificacao consentimento, CancellationToken ct = default) =>
        await db.NotifConsentimentos.AddAsync(consentimento, ct);

    public Task UpdateAsync(ConsentimentoNotificacao consentimento, CancellationToken ct = default)
    {
        db.NotifConsentimentos.Update(consentimento);
        return Task.CompletedTask;
    }
}
