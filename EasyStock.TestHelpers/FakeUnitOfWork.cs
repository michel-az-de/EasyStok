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

    public int DescartesCount { get; private set; }

    public void DescartarAlteracoesPendentes() => DescartesCount++;

    /// <summary>Quem testa a violação de unicidade decide: por padrão nenhuma falha é 23505.</summary>
    public Func<Exception, bool> EhViolacaoDeUnicidadePredicado { get; set; } = _ => false;

    public bool EhViolacaoDeUnicidade(Exception ex) => EhViolacaoDeUnicidadePredicado(ex);

    public Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken ct = default)
        => Task.FromResult<IDbTransactionScope>(new FakeTransactionScope());

    /// <summary>Verdadeiro enquanto o corpo de um <c>ExecuteInTransaction*</c> roda: prova que a leitura com trava está dentro da transação.</summary>
    public bool EmTransacao { get; private set; }

    public Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
        => EmTransacaoAsync(async () => { await action(ct); return 0; });

    public Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
        => EmTransacaoAsync(() => action(ct));

    public Task<T> ExecuteInTransactionSemRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
        => EmTransacaoAsync(() => action(ct));

    private async Task<T> EmTransacaoAsync<T>(Func<Task<T>> action)
    {
        var anterior = EmTransacao;
        EmTransacao = true;
        try { return await action(); }
        finally { EmTransacao = anterior; }
    }

    private sealed class FakeTransactionScope : IDbTransactionScope
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
