using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface INotificadorService
{
    /// <summary>
    /// Publica evento + cria entradas no outbox na mesma transação.
    /// Chamado por use cases da API para notificações event-driven imediatas.
    /// </summary>
    Task PublicarEventoAsync(
        TipoEventoNotificacao tipo,
        Guid empresaId,
        Guid? usuarioDestinoId,
        string payloadJson,
        IDictionary<string, object?>? varsAdicionais = null,
        CancellationToken ct = default);

    /// <summary>
    /// Outbox transacional (ADR-0030): enfileira um EventoNotificacao (Status=Pendente) na
    /// unit-of-work ATUAL — só estagia (AddAsync), sem processar/enviar e sem commit próprio.
    /// A linha é persistida no MESMO CommitAsync() da mutação de negócio (atomicidade); nada
    /// aguardado/falível roda após o commit. O Avaliador (loop por timer, poison-safe) consome
    /// os Pendentes via <see cref="AvaliarEventoAsync"/>. O destinatário in-app/email vai no
    /// payload (chave "usuarioId"/"email"), re-derivado pelo Avaliador.
    /// </summary>
    Task EnfileirarEventoAsync(
        TipoEventoNotificacao tipo,
        Guid empresaId,
        string payloadJson,
        Guid? refEntidadeId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Processa um EventoNotificacao já persistido, criando entradas no outbox.
    /// Chamado pelo Worker para eventos gerados por coletores/cron.
    /// </summary>
    Task AvaliarEventoAsync(
        EventoNotificacao evento,
        CancellationToken ct = default);
}
