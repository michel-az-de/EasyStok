using EasyStock.Application.Services.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Ports.Output.Notifications;

public interface IEventoNotificacaoRepository
{
    Task<EventoNotificacao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>O evento da empresa pelo id, rastreado. A empresa vai no <c>WHERE</c> porque no Worker o filtro do EF está desligado.</summary>
    Task<EventoNotificacao?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Os eventos <c>Pendente</c> mais antigos, só <c>(Id, EmpresaId)</c> (N1). Cross-tenant por natureza: o avaliador
    /// liga o bypass de RLS pela porta <c>IRowLevelSecurityBypass</c> nesta leitura curta e relê cada evento no escopo da
    /// empresa. Ignora o filtro do EF porque o gatilho HTTP da API não tem tenant.
    /// </summary>
    Task<IReadOnlyList<EventoPendente>> ListarPendentesParaAvaliarAsync(
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Quarentena (N1): os eventos <c>Pendente</c> cujo prazo de validade (pelo tipo, contado de <c>OcorridoEm</c>)
    /// passou viram <c>Expirado</c>, até <paramref name="limitePorPrazo"/> por grupo de prazo. Os tipos que carregam
    /// segredo no payload (<c>ResetSenha</c>, <c>ConfirmacaoEmail</c>) apagam o payload ao expirar. Cross-tenant: o
    /// chamador liga o bypass pela porta; o commit é dele.
    /// </summary>
    /// <returns>Os eventos que expiraram (#2875: o aviso ao cliente vira não entregue na conversa).</returns>
    Task<IReadOnlyList<NotificacaoExpirada>> ExpirarPendentesAsync(PoliticaValidadeNotificacao politica, int limitePorPrazo, CancellationToken ct = default);

    /// <summary>
    /// Já existe evento da empresa com esta <c>CorrelationId</c> (N12)? É a "última execução" das rotinas agendadas e a
    /// pré-checagem barata antes de montar o payload; a garantia final é o índice único <c>(EmpresaId, CorrelationId)</c>.
    /// A empresa vai no <c>WHERE</c> porque no Worker o filtro do EF está desligado.
    /// </summary>
    Task<bool> ExisteCorrelacaoAsync(Guid empresaId, string correlationId, CancellationToken ct = default);

    Task AddAsync(EventoNotificacao evento, CancellationToken ct = default);
    Task UpdateAsync(EventoNotificacao evento, CancellationToken ct = default);
}

/// <summary>Referência a um evento pendente: o mínimo para abrir o escopo da empresa e reler o evento.</summary>
public sealed record EventoPendente(Guid Id, Guid EmpresaId);

/// <summary>
/// Evento ou mensagem do outbox que a quarentena acabou de expirar (#2875): o mínimo para abrir o escopo da empresa,
/// reler e decidir se o aviso vai à conversa do cliente como não entregue.
/// </summary>
public sealed record NotificacaoExpirada(Guid Id, Guid EmpresaId, TipoEventoNotificacao Tipo);
