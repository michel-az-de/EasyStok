using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface ILogEnvioNotificacaoRepository
{
    Task AddAsync(LogEnvioNotificacao log, CancellationToken ct = default);

    Task<IReadOnlyList<LogEnvioNotificacao>> ListarPorOutboxAsync(
        Guid outboxMensagemId,
        CancellationToken ct = default);

    Task<(IReadOnlyList<LogEnvioNotificacao> Items, int TotalCount)> ListarAsync(
        Guid? empresaId,
        StatusOutbox? status = null,
        CanalNotificacao? canal = null,
        DateTime? de = null,
        DateTime? ate = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);
}
