using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IConsentimentoRepository
{
    Task<ConsentimentoNotificacao?> GetAsync(
        Guid usuarioId,
        CanalNotificacao canal,
        CategoriaConteudoNotificacao categoria,
        CancellationToken ct = default);

    Task<IReadOnlyList<ConsentimentoNotificacao>> ListarPorUsuarioAsync(
        Guid usuarioId,
        CancellationToken ct = default);

    /// <summary>O estado atual (o registro mais recente por canal e categoria) de cada pessoa da lista (N4).</summary>
    Task<IReadOnlyList<ConsentimentoNotificacao>> ListarPorUsuariosAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken ct = default);

    /// <summary>
    /// Anonimização (N4, LGPD): em todo o histórico do usuário, <c>OptIn = false</c>, <c>MotivoOptOut = "anonimizacao"</c> e
    /// <c>IpOrigem</c> nulo. Imediato, como a remoção dos tokens. Devolve as linhas alteradas.
    /// </summary>
    Task<int> RevogarPorAnonimizacaoAsync(Guid usuarioId, CancellationToken ct = default);

    Task AddAsync(ConsentimentoNotificacao consentimento, CancellationToken ct = default);
    Task UpdateAsync(ConsentimentoNotificacao consentimento, CancellationToken ct = default);
}
