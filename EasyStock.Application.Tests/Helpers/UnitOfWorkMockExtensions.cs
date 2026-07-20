using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.Tests.Helpers;

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

    // issue 952: analogo a SetupExecuteInTransaction<T>, mas pro metodo SEM retry
    // (ExecuteInTransactionSemRetryAsync) -- usado por use cases com creates de Guid.NewGuid
    // que nao podem ser reexecutados numa falha transitoria (RegistrarSaidaEstoque, issue 822).
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
