namespace EasyStock.Application.Ports.Output.Persistence;

public interface IUnitOfWork
{
    Task<int> CommitAsync();

    /// <summary>
    /// Inicia transação explícita. Use quando precisar manter locks
    /// pessimistas (FOR UPDATE) entre múltiplas operações até o commit.
    /// Sem isso, EF abre transação implícita só no SaveChanges, e locks
    /// adquiridos antes (ex: GetByIdComLockAsync) já foram liberados.
    /// <para>
    /// O consumidor DEVE chamar <see cref="IDbTransactionScope.CommitAsync"/>
    /// antes do fim do <c>await using</c>; caso contrário o provedor faz
    /// rollback no <c>Dispose</c>.
    /// </para>
    /// </summary>
    Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Executa <paramref name="action"/> dentro de uma transacao explicita,
    /// respeitando a retry strategy do provedor (Postgres EnableRetryOnFailure).
    /// O bloco inteiro pode ser reexecutado em falha transitoria — escreva
    /// <paramref name="action"/> de forma idempotente.
    /// <para>
    /// Use quando combinar <c>FOR UPDATE</c> + mutacao + <c>SaveChanges</c>
    /// num unico bloco atomico. <c>action</c> NAO deve abrir transacao
    /// propria — esta API ja faz Begin/Commit.
    /// </para>
    /// </summary>
    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct = default);

    /// <summary>
    /// Versão com valor de retorno. Mesmas regras de
    /// <see cref="ExecuteInTransactionAsync(Func{CancellationToken, Task}, CancellationToken)"/>:
    /// idempotente porque pode reexecutar em falha transitoria.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default);
}
