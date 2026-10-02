using EasyStock.Domain.Entities.Notifications;

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

    Task AddAsync(EventoNotificacao evento, CancellationToken ct = default);
    Task UpdateAsync(EventoNotificacao evento, CancellationToken ct = default);
}

/// <summary>Referência a um evento pendente: o mínimo para abrir o escopo da empresa e reler o evento.</summary>
public sealed record EventoPendente(Guid Id, Guid EmpresaId);
