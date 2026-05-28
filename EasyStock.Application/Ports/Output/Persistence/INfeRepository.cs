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
    ///
    /// <para>
    /// <b>⚠️ ATENÇÃO CROSS-TENANT:</b> esta query ignora o Global Query Filter via
    /// <c>IgnoreQueryFilters()</c>. Pode retornar NfeDocumento de QUALQUER empresa.
    /// SOMENTE chamar de:
    /// </para>
    /// <list type="bullet">
    ///   <item>Webhook controller validado por HMAC (sem JWT, sem contexto de tenant).</item>
    ///   <item>Jobs de contingência/reconciliação operando sob <see cref="EasyStock.Application.Ports.Output.Security.IRowLevelSecurityBypass"/>.</item>
    /// </list>
    /// <para>
    /// NUNCA chamar de request-path autenticado normal — perderia isolamento multi-tenant.
    /// </para>
    /// </summary>
    Task<NfeDocumento?> FindByChaveAcessoAsync(string chaveAcesso, CancellationToken ct = default);

    /// <summary>
    /// Busca <see cref="NfeDocumento"/> ja emitido com a mesma <paramref name="idempotencyKey"/>
    /// para a empresa. Usado pelo <c>EmitirNfceUseCase</c> ANTES de reservar numero,
    /// garantindo que retry com mesma chave nao queime segundo numero fiscal.
    ///
    /// <para>
    /// Respeita Global Query Filter (multi-tenant): so retorna documentos da
    /// <paramref name="empresaId"/> informada. Unique partial index
    /// <c>ux_nfe_documentos_empresa_idempotency</c> serve como ultima linha de
    /// defesa contra race entre 2 requests concorrentes.
    /// </para>
    /// </summary>
    Task<NfeDocumento?> FindByIdempotencyKeyAsync(Guid empresaId, string idempotencyKey, CancellationToken ct = default);

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
