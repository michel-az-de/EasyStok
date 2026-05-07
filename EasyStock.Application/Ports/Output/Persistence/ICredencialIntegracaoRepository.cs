using EasyStock.Domain.Integration;

namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Acesso CRUD à tabela <c>credencial_integracao</c>. Repositório baixo-nível —
/// callers de integração devem usar
/// <see cref="Output.Integration.Crypto.IIntegrationCredentialResolver"/>
/// que abstrai cifragem/decifragem em cima deste repo.
/// </summary>
public interface ICredencialIntegracaoRepository
{
    /// <summary>
    /// Busca a credencial ativa pra um tenant + provider + ambiente. Garantida
    /// única pelo índice filtrado em <c>(empresa_id, provider_key, ambiente)
    /// where ativo</c>. Retorna null se inexistente ou inativa.
    /// </summary>
    Task<CredencialIntegracao?> GetAtivaAsync(
        Guid empresaId,
        string providerKey,
        AmbienteIntegracao ambiente,
        CancellationToken ct = default);

    Task<CredencialIntegracao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lista todas (ativas + inativas) por empresa — pra painel admin.</summary>
    Task<IReadOnlyList<CredencialIntegracao>> ListarPorEmpresaAsync(
        Guid empresaId,
        CancellationToken ct = default);

    /// <summary>
    /// Lista credenciais com KEK específica — usado em rotação batch.
    /// Cross-tenant (use só em jobs admin com IgnoreQueryFilters).
    /// </summary>
    Task<IReadOnlyList<CredencialIntegracao>> ListarPorKekAsync(
        string kekId,
        CancellationToken ct = default);

    Task AddAsync(CredencialIntegracao credencial, CancellationToken ct = default);
    Task UpdateAsync(CredencialIntegracao credencial, CancellationToken ct = default);
}
