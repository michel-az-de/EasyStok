using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IVariavelTemplateCatalogoRepository
{
    Task<IReadOnlyList<VariavelTemplateCatalogo>> ListarPorTipoEventoAsync(
        TipoEventoNotificacao tipoEvento,
        CancellationToken ct = default);
}
