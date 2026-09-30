using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Campanhas;

namespace EasyStock.Application.Ports.Output.Persistence.Campanhas;

/// <summary>Campanhas e destinatários (S28). <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).</summary>
public interface ICampanhaRepository
{
    Task AddAsync(Campanha campanha, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<Campanha?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>Mais recentes primeiro.</summary>
    Task<IReadOnlyList<Campanha>> ListarAsync(Guid empresaId, StatusCampanha? status, int limite, CancellationToken ct = default);

    /// <summary>Destinatários ainda pendentes da campanha, rastreados (o cancelamento os exclui).</summary>
    Task<IReadOnlyList<CampanhaDestinatario>> ListarPendentesAsync(Guid empresaId, Guid campanhaId, CancellationToken ct = default);

    /// <summary>Todos os destinatários da campanha, rastreados (o cálculo do público os reclassifica).</summary>
    Task<IReadOnlyList<CampanhaDestinatario>> ListarDestinatariosAsync(Guid empresaId, Guid campanhaId, CancellationToken ct = default);

    Task AddDestinatariosAsync(IEnumerable<CampanhaDestinatario> destinatarios, CancellationToken ct = default);

    /// <summary>Tira da campanha quem saiu do público (só pendentes e excluídos chegam aqui).</summary>
    void RemoverDestinatarios(IEnumerable<CampanhaDestinatario> destinatarios);

    /// <summary>
    /// Destinatários <see cref="StatusCampanhaDestinatario.Enfileirado"/> com a mensagem do outbox
    /// (nula se já expurgada), ambos rastreados: o job concilia o envio (S30) e o cancelamento tira da fila.
    /// </summary>
    Task<IReadOnlyList<EnvioDestinatarioCampanha>> ListarEnfileiradosAsync(Guid empresaId, Guid campanhaId, CancellationToken ct = default);

    /// <summary>Destinatários <see cref="StatusCampanhaDestinatario.Enviado"/> (sem pedido), para o lembrete do encerramento.</summary>
    Task<IReadOnlyList<CampanhaDestinatario>> ListarEnviadosAsync(Guid empresaId, Guid campanhaId, CancellationToken ct = default);

    /// <summary>
    /// Envio mais recente ao cliente, com <c>EnviadoEm</c> desde <paramref name="desde"/>, de campanha
    /// <see cref="StatusCampanha.Enviando"/> ou <see cref="StatusCampanha.Enviada"/>. Rastreado: a atribuição
    /// do pedido o marca como <see cref="StatusCampanhaDestinatario.Pediu"/>.
    /// </summary>
    Task<CampanhaDestinatario?> ObterEnviadoParaAtribuirAsync(Guid empresaId, Guid clienteId, DateTime desde, CancellationToken ct = default);

    /// <summary>
    /// Cross-tenant, para o job (S30), com o bypass de RLS ligado: agendadas vencidas, as que estão
    /// enviando (conciliar o outbox) e as que passaram do encerramento.
    /// </summary>
    Task<IReadOnlyList<CampanhaParaProcessar>> ListarParaProcessarAsync(DateTime agora, int limite, CancellationToken ct = default);
}

/// <summary>Destinatário enfileirado e a mensagem que o leva; <see cref="Mensagem"/> nula se o outbox foi expurgado.</summary>
public sealed record EnvioDestinatarioCampanha(CampanhaDestinatario Destinatario, OutboxMensagemNotificacao? Mensagem);

public sealed record CampanhaParaProcessar(Guid EmpresaId, Guid CampanhaId);
