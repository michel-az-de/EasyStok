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

            try
            {
                foreach (var operation in pending)
                    await operation(session, CancellationToken.None);

                await session.CommitTransactionAsync();
            }
            catch (Exception)
            {
                await session.AbortTransactionAsync();
                throw;
            }

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
        ex is MongoCommandException mongoCommandException && (
            mongoCommandException.CodeName?.Contains("Transaction", StringComparison.OrdinalIgnoreCase) == true ||
            mongoCommandException.Message.Contains("Transaction", StringComparison.OrdinalIgnoreCase) ||
            mongoCommandException.Message.Contains("replica set", StringComparison.OrdinalIgnoreCase)) ||
        ex is MongoClientException mongoClientException &&
        mongoClientException.Message.Contains("Transaction", StringComparison.OrdinalIgnoreCase);

    // Mongo já tem transação implícita via session — Begin é no-op aqui.
    public Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken ct = default)
        => Task.FromResult<IAsyncDisposable>(new NoopScope());

    private sealed class NoopScope : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    }
