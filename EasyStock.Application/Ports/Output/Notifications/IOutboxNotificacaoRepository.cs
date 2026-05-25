using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IOutboxNotificacaoRepository
{
    Task<OutboxMensagemNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<OutboxMensagemNotificacao>> ListarPendentesParaProcessarAsync(
        int shardKey,
        int batchSize,
        CancellationToken ct = default);

    Task<bool> ExisteAsync(string idempotencyKey, CancellationToken ct = default);

    Task<(IReadOnlyList<OutboxMensagemNotificacao> Items, int TotalCount)> ListarAsync(
        Guid? empresaId,
        StatusOutbox? status = null,
        CanalNotificacao? canal = null,
        DateTime? de = null,
        DateTime? ate = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task AddAsync(OutboxMensagemNotificacao mensagem, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<OutboxMensagemNotificacao> mensagens, CancellationToken ct = default);
    Task UpdateAsync(OutboxMensagemNotificacao mensagem, CancellationToken ct = default);
}
