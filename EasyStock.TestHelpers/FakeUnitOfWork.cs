using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.TestHelpers;

/// <summary>
/// Fake reusavel de <see cref="IUnitOfWork"/> para testes unitarios.
/// Substitui o uso anterior de <c>Substitute.For&lt;IUnitOfWork&gt;()</c>: NSubstitute
/// nao invoca o lambda passado em <see cref="IUnitOfWork.ExecuteInTransactionAsync{T}"/>,
/// fazendo com que o corpo da transacao nunca rode e os mocks de repos
/// fiquem com asserts falsos. Aqui o callback executa sempre.
///
/// <para>
/// <see cref="CommitCount"/> permite asserts equivalentes a
/// <c>uow.Received(1).CommitAsync()</c>.
/// </para>
/// </summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int CommitCount { get; private set; }

    public Task<int> CommitAsync()
    {
        CommitCount++;
        return Task.FromResult(0);
    }

    public Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken ct = default)
        => Task.FromResult<IDbTransactionScope>(new FakeTransactionScope());

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
        => action(ct);

    public Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
        => action(ct);

    private sealed class FakeTransactionScope : IDbTransactionScope
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
