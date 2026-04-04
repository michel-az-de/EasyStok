using EasyStock.Application.Ports.Output.Persistence;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Data;

public sealed class MongoUnitOfWork(IMongoClient mongoClient) : IUnitOfWork
{
    private readonly List<Func<IClientSessionHandle?, CancellationToken, Task>> _operations = [];

    public void Enqueue(Func<IClientSessionHandle?, CancellationToken, Task> operation)
    {
        _operations.Add(operation);
    }

    public async Task<int> CommitAsync()
    {
        var pending = _operations.ToArray();

        if (pending.Length == 0)
            return 0;

        try
        {
            using var session = await mongoClient.StartSessionAsync();
            session.StartTransaction();

            foreach (var operation in pending)
                await operation(session, CancellationToken.None);

            await session.CommitTransactionAsync();
            _operations.Clear();
            return pending.Length;
        }
        catch (Exception ex) when (SupportsFallback(ex))
        {
            foreach (var operation in pending)
                await operation(null, CancellationToken.None);

            _operations.Clear();
            return pending.Length;
        }
    }

    private static bool SupportsFallback(Exception ex) =>
        ex is NotSupportedException ||
        ex is MongoClientException ||
        ex is MongoCommandException;
    }
