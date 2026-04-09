using System.Text.RegularExpressions;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public abstract class MongoRepositoryBase
{
    protected MongoRepositoryBase(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    {
        Context = context;
        UnitOfWork = unitOfWork;
    }

    protected MongoEasyStockContext Context { get; }
    protected MongoUnitOfWork UnitOfWork { get; }

    protected static string BuildContainsPattern(string value) => Regex.Escape(value.Trim());

    protected void EnqueueInsert<TDocument>(IMongoCollection<TDocument> collection, TDocument document) =>
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? collection.InsertOneAsync(document, cancellationToken: ct)
                : collection.InsertOneAsync(session, document, cancellationToken: ct));

    protected void EnqueueReplace<TDocument>(IMongoCollection<TDocument> collection, Guid id, TDocument document) =>
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? collection.ReplaceOneAsync(Builders<TDocument>.Filter.Eq("Id", id), document, cancellationToken: ct)
                : collection.ReplaceOneAsync(session, Builders<TDocument>.Filter.Eq("Id", id), document, cancellationToken: ct));

    protected void EnqueueDelete<TDocument>(IMongoCollection<TDocument> collection, Guid id) =>
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? collection.DeleteOneAsync(Builders<TDocument>.Filter.Eq("Id", id), ct)
                : collection.DeleteOneAsync(session, Builders<TDocument>.Filter.Eq("Id", id), null, ct));
}
