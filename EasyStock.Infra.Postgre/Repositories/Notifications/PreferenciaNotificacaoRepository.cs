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

    public async Task DefinirAsync(
        Guid usuarioId, Guid empresaId, string rotinaCodigo, bool habilitada, CancellationToken ct = default)
    {
        var atual = await db.NotifPreferenciasUsuario
            .FirstOrDefaultAsync(p => p.UsuarioId == usuarioId && p.RotinaCodigo == rotinaCodigo, ct);
        if (atual is null)
        {
            db.NotifPreferenciasUsuario.Add(PreferenciaNotificacaoUsuario.Criar(usuarioId, empresaId, rotinaCodigo, habilitada));
            return;
        }

        atual.EmpresaId = empresaId;
        atual.Atualizar(habilitada, atual.CanalPreferido);
    }

    public Task<int> RemoverPorUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        db.NotifPreferenciasUsuario.Where(p => p.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);
}
