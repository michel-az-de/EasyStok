using EasyStock.Application.Ports.Output.Persistence;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Helpers;

/// <summary>
/// IUnitOfWork.ExecuteInTransactionAsync e a barreira que envolve os use cases.
/// Substitute padrao retorna default(T) sem invocar a action — testes que
/// dependem do corpo do use case rodando precisam configurar o mock pra
/// realmente invocar o callback. Esses helpers fazem isso.
/// </summary>
internal static class UnitOfWorkMockExtensions
{
    public static IUnitOfWork SetupExecuteInTransaction<T>(this IUnitOfWork uow)
    {
        uow.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<T>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var action = call.Arg<Func<CancellationToken, Task<T>>>();
                var ct = call.Arg<CancellationToken>();
                return action(ct);
            });
        return uow;
    }

    public static IUnitOfWork SetupExecuteInTransaction(this IUnitOfWork uow)
    {
        uow.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var action = call.Arg<Func<CancellationToken, Task>>();
                var ct = call.Arg<CancellationToken>();
                return action(ct);
            });
        return uow;
    }

    /// <summary>
    /// Espelha <see cref="SetupExecuteInTransaction{T}"/> para o caminho SEM retry
    /// (issue 952): use cases que criam Guids novos no corpo nao podem ser re-executados
    /// pelo IExecutionStrategy — o retry duplicava a venda. Quem migrou para
    /// ExecuteInTransactionSemRetryAsync precisa deste setup, senao o Substitute
    /// devolve default(T) sem invocar a action e o teste falha com NullReference.
    /// </summary>
    public static IUnitOfWork SetupExecuteInTransactionSemRetry<T>(this IUnitOfWork uow)
    {
        uow.ExecuteInTransactionSemRetryAsync(
                Arg.Any<Func<CancellationToken, Task<T>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var action = call.Arg<Func<CancellationToken, Task<T>>>();
                var ct = call.Arg<CancellationToken>();
                return action(ct);
            });
        return uow;
    }
}
