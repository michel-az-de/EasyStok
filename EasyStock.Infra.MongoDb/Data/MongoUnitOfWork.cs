using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Infra.MongoDb.Data;

public sealed class MongoUnitOfWork : IUnitOfWork
{
    private readonly List<Func<CancellationToken, Task>> _operations = [];

    public void Enqueue(Func<CancellationToken, Task> operation)
    {
        _operations.Add(operation);
    }

    public async Task<int> CommitAsync()
    {
        var pending = _operations.ToArray();
        _operations.Clear();

        foreach (var operation in pending)
            await operation(CancellationToken.None);

        return pending.Length;
    }
}
