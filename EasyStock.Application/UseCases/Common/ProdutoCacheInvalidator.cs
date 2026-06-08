namespace EasyStock.Application.UseCases.Common;

/// <summary>
/// Ponto canonico de invalidacao do cache de saldo de produtos. Chamado pelo
/// <c>EstoqueSaldoCacheInvalidationInterceptor</c> (chokepoint de SaveChanges) —
/// nenhum use case ou servico precisa lembrar de invalidar.
///
/// <para>
/// Best-effort (G2): a invalidacao roda APOS a persistencia; uma falha de cache
/// (ex: Redis indisponivel) NAO pode lancar, senao o caller veria erro numa
/// operacao que JA persistiu e re-tentaria, dobrando o saldo. Loga (alarma) e segue.
/// </para>
///
/// <para>
/// Fatia 1: invalida o produto-detalhe via <see cref="CacheKeys.ProdutoRelacionadas"/>
/// (superset de <c>produto:{e}:{p}</c>). Fatia 2 (#462) adiciona aqui o bump da
/// geracao analytics, DEPOIS desta op critica (failure-order: produto-detalhe e
/// a chave de 5min; analytics auto-cura em 60s).
/// </para>
/// </summary>
public sealed class ProdutoCacheInvalidator(
    ICacheService cache,
    ILogger<ProdutoCacheInvalidator> logger) : IProdutoCacheInvalidator
{
    public async Task InvalidarSaldoAsync(Guid empresaId, IEnumerable<Guid> produtoIds)
    {
        // G4: produtos distintos; uma falha num produto nao deixa os outros stale.
        foreach (var produtoId in produtoIds.Distinct())
        {
            try
            {
                await cache.RemoveAsync(CacheKeys.ProdutoRelacionadas(empresaId, produtoId));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "ProdutoCacheInvalidator: falha ao invalidar cache de saldo (empresa={EmpresaId}, produto={ProdutoId}). Saldo pode ficar stale ate o TTL.",
                    empresaId, produtoId);
            }
        }
    }
}
