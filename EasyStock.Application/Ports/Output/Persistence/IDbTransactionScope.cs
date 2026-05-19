namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Escopo de transação explícita. Semântica:
/// <list type="bullet">
///   <item>O consumidor DEVE chamar <see cref="CommitAsync"/> antes do fim do <c>await using</c>.</item>
///   <item>Se o escopo for <c>Dispose</c>d sem commit (exceção, return prematuro, esquecimento), o
///     provedor faz <c>rollback</c>.</item>
///   <item>Combinar com <see cref="IUnitOfWork.CommitAsync"/> (que chama <c>SaveChanges</c>)
///     antes do <see cref="CommitAsync"/> deste escopo.</item>
/// </list>
/// </summary>
public interface IDbTransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct = default);
}
