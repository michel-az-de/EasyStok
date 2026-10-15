using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class PreferenciaNotificacaoRepository(EasyStockDbContext db) : IPreferenciaNotificacaoRepository
{
    public async Task<IReadOnlyList<PreferenciaNotificacaoUsuario>> ListarPorUsuarioAsync(
        Guid usuarioId, CancellationToken ct = default) =>
        await db.NotifPreferenciasUsuario.AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId)
            .OrderBy(p => p.RotinaCodigo)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PreferenciaNotificacaoUsuario>> ListarDaRotinaAsync(
        Guid empresaId, string rotinaCodigo, IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default) =>
        usuarioIds.Count == 0
            ? []
            : await db.NotifPreferenciasUsuario.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId && p.RotinaCodigo == rotinaCodigo && usuarioIds.Contains(p.UsuarioId))
                .ToListAsync(ct);

    public Task<int> RemoverPorUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        db.NotifPreferenciasUsuario.Where(p => p.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);
}
