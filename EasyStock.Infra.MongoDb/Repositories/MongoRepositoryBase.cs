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
        UnitOfWork.Enqueue(ct => collection.InsertOneAsync(document, cancellationToken: ct));

    protected void EnqueueReplace<TDocument>(IMongoCollection<TDocument> collection, Guid id, TDocument document) =>
        UnitOfWork.Enqueue(ct => collection.ReplaceOneAsync(Builders<TDocument>.Filter.Eq("Id", id), document, cancellationToken: ct));

    protected void EnqueueDelete<TDocument>(IMongoCollection<TDocument> collection, Guid id) =>
        UnitOfWork.Enqueue(ct => collection.DeleteOneAsync(Builders<TDocument>.Filter.Eq("Id", id), ct));
}
