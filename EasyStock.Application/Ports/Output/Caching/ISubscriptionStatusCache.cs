using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Caching;

/// <summary>
/// Snapshot imutavel dos campos da assinatura ativa que o
/// <c>SubscriptionGateMiddleware</c> precisa para decidir bloqueio (402).
/// E um value type explicito pra evitar cachear a entidade
/// <c>AssinaturaEmpresa</c> rastreada por DbContext entre requests
/// (cross-request entity tracking = bug horrivel).
///
/// <para><c>NaoEncontrada=true</c> sinaliza tenant SEM assinatura ativa
/// (cacheia o "nao existe" pra evitar bater no DB toda request).</para>
/// </summary>
public sealed record SubscriptionStatusSnapshot(
    StatusAssinatura Status,
    DateTime? TrialFim,
    DateTime? DataFim,
    bool NaoEncontrada = false
)
{
    public static SubscriptionStatusSnapshot Vazio { get; } =
        new(StatusAssinatura.Cancelada, null, null, NaoEncontrada: true);
}

/// <summary>
/// Cache de snapshot da assinatura ativa por <c>empresaId</c>, com TTL
/// curto (default 60s). Usado pelo <c>SubscriptionGateMiddleware</c> para
/// evitar uma query EF Core a cada request autenticada.
///
/// <para>
/// Invalidacao e disparada por <c>AssinaturaCacheInvalidationInterceptor</c>
/// (SaveChangesInterceptor) sempre que uma <c>AssinaturaEmpresa</c> e
/// adicionada/modificada/deletada e o SaveChanges sucede. Como o TTL e
/// curto e o estado convergente, falhar uma invalidacao apenas adia a
/// propagacao por &lt;= TTL — nao viola a corretude do gate.
/// </para>
/// </summary>
public interface ISubscriptionStatusCache
{
    /// <summary>
    /// Retorna o snapshot do tenant. Se ja em cache (hit), serve em &lt; 1ms
    /// sem chamar <paramref name="fetch"/>. Se miss, chama <paramref name="fetch"/>,
    /// armazena por TTL e retorna. Ignora cache se <paramref name="empresaId"/>
    /// for <see cref="Guid.Empty"/>.
    /// </summary>
    Task<SubscriptionStatusSnapshot?> GetOrFetchAsync(
        Guid empresaId,
        Func<CancellationToken, Task<SubscriptionStatusSnapshot?>> fetch,
        CancellationToken ct = default);

    /// <summary>Remove a entrada do cache do tenant. No-op se ausente.</summary>
    void Invalidate(Guid empresaId);
}
