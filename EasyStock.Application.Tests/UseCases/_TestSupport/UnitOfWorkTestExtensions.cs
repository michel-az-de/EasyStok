using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Helpers para mocks de <see cref="IUnitOfWork"/>. NSubstitute por padrao
/// retorna <c>default(Task&lt;T&gt;)</c> em <c>ExecuteInTransactionAsync</c>,
/// fazendo o lambda interno NUNCA executar — o teste falha com NRE no
/// resultado <c>null</c>. Estes helpers configuram o mock para invocar o
/// lambda como se fosse a implementacao real (sem retry).
/// </summary>
internal static class UnitOfWorkTestExtensions
{
    public static void SetupExecuteInTransactionForward<T>(this IUnitOfWork unitOfWork)
    {
        unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<T>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var action = (Func<CancellationToken, Task<T>>)call[0];
                var ct = (CancellationToken)call[1];
                return action(ct);
            });
    }

    public static void SetupExecuteInTransactionForward(this IUnitOfWork unitOfWork)
    {
        unitOfWork
            .ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var action = (Func<CancellationToken, Task>)call[0];
                var ct = (CancellationToken)call[1];
                return action(ct);
            });
    }

    // issue 952: RegistrarSaidaEstoqueUseCase trocou ExecuteInTransactionAsync (com retry) por
    // ExecuteInTransactionSemRetryAsync -- retry reexecutava o delegate no mesmo DbContext sem
    // ChangeTracker.Clear, duplicando Venda/ItemVenda/MovimentacaoEstoque e rebaixando o mesmo
    // ItemEstoque tracked 2x numa falha transitoria pos-SaveChanges. Sem este forward, o mock
    // devolve default(Task<T>) pro metodo que o use case agora chama de verdade, e todo teste
    // falha com resultado null (mesmo pitfall documentado no metodo acima, so que pro SemRetry).
    public static void SetupExecuteInTransactionSemRetryForward<T>(this IUnitOfWork unitOfWork)
    {
        unitOfWork
            .ExecuteInTransactionSemRetryAsync(
                Arg.Any<Func<CancellationToken, Task<T>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var action = (Func<CancellationToken, Task<T>>)call[0];
                var ct = (CancellationToken)call[1];
                return action(ct);
            });
    }
}
