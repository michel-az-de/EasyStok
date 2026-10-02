using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface ITemplateRepository
{
    Task<TemplateNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Busca o template ativo para um código+canal. Tenta empresa-specific primeiro,
    /// depois global (EmpresaId = null) como fallback.
    /// </summary>
    Task<TemplateNotificacao?> GetAtivoAsync(
        string codigo,
        CanalNotificacao canal,
        Guid? empresaId,
        CancellationToken ct = default);

    /// <summary>
    /// Template ativo e aprovado do tipo de evento no canal (N5), para quando o código da rotina não existe naquele
    /// canal. A empresa vem antes do global; dentro de cada um, a maior <c>Versao</c>. Com <paramref name="empresaId"/>
    /// nulo, só o global.
    /// </summary>
    Task<TemplateNotificacao?> GetAtivoPorTipoAsync(
        TipoEventoNotificacao tipo,
        CanalNotificacao canal,
        Guid? empresaId,
        CancellationToken ct = default);

    Task<(IReadOnlyList<TemplateNotificacao> Items, int TotalCount)> ListarAsync(
        Guid? empresaId,
        TipoEventoNotificacao? tipoEvento = null,
        CanalNotificacao? canal = null,
        bool? ativo = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task AddAsync(TemplateNotificacao template, CancellationToken ct = default);
    Task UpdateAsync(TemplateNotificacao template, CancellationToken ct = default);
}
