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
        Guid usuarioId, CancellationToken ct = default) =>
        await db.NotifConsentimentos
            .AsNoTracking()
            .Where(c => c.UsuarioId == usuarioId)
            // Último registro por (canal, categoria) representa o estado atual
            .GroupBy(c => new { c.Canal, c.Categoria })
            .Select(g => g.OrderByDescending(c => c.AtualizadoEm).First())
            .ToListAsync(ct);

    public async Task AddAsync(ConsentimentoNotificacao consentimento, CancellationToken ct = default) =>
        await db.NotifConsentimentos.AddAsync(consentimento, ct);

    public Task UpdateAsync(ConsentimentoNotificacao consentimento, CancellationToken ct = default)
    {
        db.NotifConsentimentos.Update(consentimento);
        return Task.CompletedTask;
    }
}
