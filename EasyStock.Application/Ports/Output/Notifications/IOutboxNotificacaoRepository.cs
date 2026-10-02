using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IOutboxNotificacaoRepository
{
    Task<OutboxMensagemNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Claim do dispatcher (N1): pega as <c>Pendente</c> elegíveis (<c>ProximaTentativaEm</c> vencida) com
    /// <c>FOR UPDATE SKIP LOCKED</c> e as passa para <c>EmEnvio</c> com lease, devolvendo as entidades rastreadas. Dois
    /// dispatchers nunca pegam a mesma mensagem. Cross-tenant por natureza: o chamador liga o bypass de RLS pela porta
    /// <c>IRowLevelSecurityBypass</c> antes de qualquer conexão e roda dentro de uma transação curta (o commit é dele).
    /// </summary>
    Task<IReadOnlyList<OutboxMensagemNotificacao>> ReservarParaEnvioAsync(int limite, CancellationToken ct = default);

    /// <summary>
    /// Quarentena (N1): as mensagens <c>Pendente</c> sem tentativa cujo prazo de validade (pelo tipo do evento, contado de
    /// <c>ProximaTentativaEm</c>) passou viram <c>Expirado</c>, até <paramref name="limitePorPrazo"/> por grupo de prazo.
    /// Roda antes da reserva, na mesma transação: não há janela em que o backlog velho seja elegível. Cross-tenant: o
    /// chamador liga o bypass pela porta; o commit é dele.
    /// </summary>
    /// <returns>Quantas mensagens expiraram.</returns>
    Task<int> ExpirarPendentesAsync(PoliticaValidadeNotificacao politica, int limitePorPrazo, CancellationToken ct = default);

    /// <summary>
    /// Lease (N1): as mensagens <c>EmEnvio</c> com o lease vencido saem do limbo (<c>FOR UPDATE SKIP LOCKED</c>). E-mail,
    /// in-app e push voltam a <c>Pendente</c>; WhatsApp e SMS viram <c>Indeterminado</c> e nunca voltam à fila. Mesmas
    /// regras do <see cref="ExpirarPendentesAsync"/> quanto a bypass e commit.
    /// </summary>
    /// <returns>Quantas mensagens foram reclamadas.</returns>
    Task<int> ReclamarLeasesVencidosAsync(int limite, CancellationToken ct = default);

    /// <summary>A mensagem da empresa pelo id, rastreada. A empresa vai no <c>WHERE</c> porque no Worker o filtro do EF está desligado.</summary>
    Task<OutboxMensagemNotificacao?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<bool> ExisteAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// O evento ainda tem outra mensagem aberta (<c>Pendente</c> ou <c>EmEnvio</c>) além de <paramref name="exceto"/>?
    /// O dispatcher usa para só apagar o payload de um evento de segurança quando termina a última mensagem
    /// dele (N2).
    /// </summary>
    Task<bool> ExisteMensagemAbertaDoEventoAsync(
        Guid empresaId,
        Guid eventoId,
        Guid exceto,
        CancellationToken ct = default);

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
