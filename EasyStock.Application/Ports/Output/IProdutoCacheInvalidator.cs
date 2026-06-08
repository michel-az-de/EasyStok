namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Invalida o cache de saldo dos produtos afetados por uma mutacao de estoque.
/// Chamado pelo interceptor de SaveChanges (chokepoint) — nenhum use case ou
/// servico precisa lembrar de invalidar.
///
/// <para>
/// Best-effort: NUNCA lanca. A invalidacao roda APOS a persistencia; uma falha
/// de cache nao pode derrubar uma operacao ja comitada (o caller re-tentaria e
/// dobraria o saldo).
/// </para>
/// </summary>
public interface IProdutoCacheInvalidator
{
    /// <summary>Invalida o cache de saldo (produto-detalhe) dos produtos informados.</summary>
    Task InvalidarSaldoAsync(Guid empresaId, IEnumerable<Guid> produtoIds);
}
