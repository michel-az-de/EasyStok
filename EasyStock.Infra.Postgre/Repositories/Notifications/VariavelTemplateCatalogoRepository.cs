using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class VariavelTemplateCatalogoRepository(EasyStockDbContext db)
    : IVariavelTemplateCatalogoRepository
{
    public async Task<IReadOnlyList<VariavelTemplateCatalogo>> ListarPorTipoEventoAsync(
        TipoEventoNotificacao tipoEvento, CancellationToken ct = default) =>
        await db.NotifVariaveisTemplate.AsNoTracking()
            .Where(v => v.TipoEvento == tipoEvento)
            .OrderBy(v => v.NomeVariavel)
            .ToListAsync(ct);
}
