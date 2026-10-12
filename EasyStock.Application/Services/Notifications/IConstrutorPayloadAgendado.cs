using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Monta o payload de um tipo de evento agendado (N12). O coletor de rotinas agendadas é genérico: cada tipo novo só
/// acrescenta um construtor registrado por <see cref="Tipo"/>. Roda no escopo de DI da empresa, com o tenant ligado.
/// </summary>
public interface IConstrutorPayloadAgendado
{
    TipoEventoNotificacao Tipo { get; }

    /// <summary>O JSON do payload do evento do dia <paramref name="diaLocal"/> (data civil de Brasília) da empresa.</summary>
    Task<string> ConstruirAsync(Guid empresaId, DateOnly diaLocal, CancellationToken ct = default);
}
