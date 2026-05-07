using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Data;

public sealed class MongoUnitOfWork(IMongoClient mongoClient, ILogger<MongoUnitOfWork> logger) : IUnitOfWork
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
            catch (Exception ex)
            {
                logger.LogError(ex, "MongoUnitOfWork: erro durante CommitTransactionAsync, abortando ({Count} operacoes pendentes).", pending.Length);
                await session.AbortTransactionAsync();
                throw;
            }

            _operations.Clear();
            return pending.Length;
        }
        catch (Exception ex) when (SupportsFallback(ex))
        {
            // Mongo standalone (sem replica set) não suporta transações. Cai em modo
            // best-effort sem rollback — operador deve estar ciente.
            logger.LogWarning(ex, "MongoUnitOfWork: transacoes nao suportadas pelo cluster; aplicando {Count} operacoes em modo nao-transacional.", pending.Length);

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
    public Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken ct = default)
        => Task.FromResult<IDbTransactionScope>(new NoopScope());

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken ct = default)
    {
        // Mongo nao tem retry strategy do EF; rodamos direto. Caller agrega
        // operacoes via Enqueue + CommitAsync.
        await action(ct);
    }

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        return await action(ct);
    }

    private sealed class NoopScope : IDbTransactionScope
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    }
