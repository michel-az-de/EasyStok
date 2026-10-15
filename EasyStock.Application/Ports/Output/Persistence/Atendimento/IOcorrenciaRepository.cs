using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>
/// Persistência de <see cref="Ocorrencia"/> (S27). Não faz SaveChanges: o commit é do <c>IUnitOfWork</c>.
/// <c>empresaId</c> vai no WHERE de toda consulta (ADR-0010).
/// </summary>
public interface IOcorrenciaRepository
{
    Task AddAsync(Ocorrencia ocorrencia, CancellationToken ct = default);

    /// <summary>Rastreada; null quando não é da empresa.</summary>
    Task<Ocorrencia?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<Ocorrencia?> ObterTravadaAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Ocorrencia>> ListarDoPedidoAsync(Guid empresaId, Guid pedidoId, CancellationToken ct = default);

    /// <summary>Mais recentes primeiro.</summary>
    Task<IReadOnlyList<Ocorrencia>> ListarAsync(Guid empresaId, StatusOcorrencia? status, int limite, CancellationToken ct = default);
}
