using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Ports.Output.Persistence.Atendimento;

/// <summary>Sessões do chat do site (S36). O token nunca é gravado: a busca é pelo hash.</summary>
public interface ISessaoChatSiteRepository
{
    Task AddAsync(SessaoChatSite sessao, CancellationToken ct = default);

    Task<SessaoChatSite?> ObterPorTokenHashAsync(Guid empresaId, string tokenHash, CancellationToken ct = default);

    /// <summary>Apaga, de todas as empresas, as sessões vencidas antes de <paramref name="limiteUtc"/>. Roda com bypass de RLS.</summary>
    Task<int> RemoverVencidasAsync(DateTime limiteUtc, CancellationToken ct = default);
}
