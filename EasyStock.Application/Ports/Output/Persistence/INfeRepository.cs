using EasyStock.Domain.Fiscal;

namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Repositorio do agregado <see cref="NfeDocumento"/>. Multi-tenant: todo
/// metodo recebe <c>empresaId</c> e a query respeita Global Query Filter.
///
/// <para>
/// Itens e Eventos sao carregados via navigation properties quando o caller
/// pede explicitamente (GetByIdWithDetailsAsync). Para queries de listagem
/// (GetByEmpresaAsync) so retorna a raiz — caller projeta para DTO.
/// </para>
/// </summary>
public interface INfeRepository
{
    Task<NfeDocumento?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>Carrega NfeDocumento com Itens + Eventos — para tela de detalhe.</summary>
    Task<NfeDocumento?> GetByIdWithDetailsAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Busca por chave de acesso. Usado pelo webhook (que NAO tem JWT, recebe a chave
    /// no payload assinado e precisa localizar o NfeDocumento correspondente).
    /// Caller webhook deve fazer com bypass RLS — chave nao traz tenant.
    /// </summary>
    Task<NfeDocumento?> FindByChaveAcessoAsync(string chaveAcesso, CancellationToken ct = default);

    // TODO F1.5: FindByIdempotencyKeyAsync — requer coluna IdempotencyKey via migration AddNfeF1RepoIndexes.

    Task<(IEnumerable<NfeDocumento> items, int total)> GetByEmpresaAsync(
        Guid empresaId,
        int page,
        int pageSize,
        StatusNfe? status = null,
        DateTime? desde = null,
        DateTime? ate = null,
        string? search = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lista NfeDocumentos em <see cref="StatusNfe.FalhaTransiente"/> aguardando
    /// reprocessamento. Usado pelo job de contingencia. Caller deve bypassar RLS
    /// (job opera cross-tenant).
    /// </summary>
    Task<IEnumerable<NfeDocumento>> ListarPendentesContingenciaAsync(int max = 100, CancellationToken ct = default);

    Task AddAsync(NfeDocumento nfe, CancellationToken ct = default);

    Task UpdateAsync(NfeDocumento nfe, CancellationToken ct = default);
}
