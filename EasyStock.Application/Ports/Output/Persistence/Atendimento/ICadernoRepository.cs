using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>S54: trechos do caderno da loja, sempre filtrados por empresa.</summary>
public interface ICadernoRepository
{
    Task AddAsync(TrechoCaderno trecho, CancellationToken ct = default);

    /// <summary>Rastreado.</summary>
    Task<TrechoCaderno?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>Sem rastreio, em ordem de título (ordem estável para o prompt do agente).</summary>
    Task<IReadOnlyList<TrechoCaderno>> ListarAsync(Guid empresaId, bool incluirArquivados, CancellationToken ct = default);
}
