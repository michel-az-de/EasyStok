using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class PreferenciaMenuRepository(EasyStockDbContext dbContext) : IPreferenciaMenuRepository
{
    public Task<PreferenciaMenuUsuario?> GetAsync(Guid usuarioId, Guid lojaId) =>
        dbContext.PreferenciasMenuUsuario
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UsuarioId == usuarioId && x.LojaId == lojaId);

    public Task AddAsync(PreferenciaMenuUsuario preferencia) =>
        dbContext.PreferenciasMenuUsuario.AddAsync(preferencia).AsTask();

    public Task UpdateAsync(PreferenciaMenuUsuario preferencia)
    {
        dbContext.PreferenciasMenuUsuario.Update(preferencia);
        return Task.CompletedTask;
    }
}
