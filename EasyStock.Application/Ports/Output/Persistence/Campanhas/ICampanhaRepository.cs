using EasyStock.Domain.Entities.Campanhas;
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
}
